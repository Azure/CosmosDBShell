// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class CosmosCommandTests
{
    [Fact]
    public async Task ExecuteCosmosCommandAsync_RecordsChargeAndClearsStaleCharge()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState { RequestCharge = 9 };

        state = await shell.ExecuteCosmosCommandAsync(new TestCosmosCommand(2.5), state, string.Empty, CancellationToken.None);
        state = await shell.ExecuteCosmosCommandAsync(new TestCosmosCommand(null), state, string.Empty, CancellationToken.None);

        Assert.Null(state.RequestCharge);
        Assert.Equal(2.5, shell.SessionRequestCharge);
        Assert.Equal(1, shell.SessionChargedOperationCount);
    }

    [Fact]
    public async Task ExecuteCosmosCommandAsync_CombinesExplicitAndScopedChargesOnce()
    {
        using var shell = ShellInterpreter.CreateInstance();

        var state = await shell.ExecuteCosmosCommandAsync(
            new TestCosmosCommand(2.5, scopedRequestCharge: 1.25),
            new CommandState(),
            string.Empty,
            CancellationToken.None);

        Assert.Equal(3.75, state.RequestCharge);
        Assert.Equal(3.75, shell.SessionRequestCharge);
        Assert.Equal(1, shell.SessionChargedOperationCount);
    }

    [Fact]
    public async Task ExecuteCosmosCommandAsync_ThrownCommandPreservesScopedCharge()
    {
        using var shell = ShellInterpreter.CreateInstance();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => shell.ExecuteCosmosCommandAsync(
            new TestCosmosCommand(null, scopedRequestCharge: 1.5, throwAfterRecording: true),
            new CommandState(),
            string.Empty,
            CancellationToken.None));

        Assert.Equal(1.5, RequestChargeContext.GetExceptionCharge(exception));
        Assert.Equal(1.5, shell.SessionRequestCharge);
        Assert.Equal(1, shell.SessionChargedOperationCount);
    }

    [Fact]
    public async Task ExecuteCosmosCommandAsync_CancelledCommandPreservesScopedCharge()
    {
        using var shell = ShellInterpreter.CreateInstance();

        await Assert.ThrowsAsync<OperationCanceledException>(() => shell.ExecuteCosmosCommandAsync(
            new TestCosmosCommand(null, scopedRequestCharge: 1.5, cancelAfterRecording: true),
            new CommandState(),
            string.Empty,
            CancellationToken.None));

        Assert.Equal(1.5, shell.SessionRequestCharge);
        Assert.Equal(1, shell.SessionChargedOperationCount);
    }

    [Fact]
    public async Task ExecuteCosmosCommandAsync_RunsInsideSerializedOperation()
    {
        using var shell = ShellInterpreter.CreateInstance();

        var state = await shell.RunSerializedAsync(
            () => shell.ExecuteCosmosCommandAsync(new TestCosmosCommand(2.5), new CommandState(), string.Empty, CancellationToken.None),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2.5, state.RequestCharge);
    }

    [Fact]
    public void CreatePartitionKey_WithHierarchicalIntegerComponents_PreservesIntegerTypes()
    {
        using var document = JsonDocument.Parse("""
        {
          "tenantId": 42,
          "userId": 9007199254740993,
          "category": "volcano"
        }
        """);

        var elements = new[]
        {
            document.RootElement.GetProperty("tenantId"),
            document.RootElement.GetProperty("userId"),
            document.RootElement.GetProperty("category"),
        };

        var expected = new PartitionKeyBuilder()
            .Add(42)
            .Add(9007199254740993L)
            .Add("volcano")
            .Build();

        var actual = TestCosmosCommand.CreatePartitionKeyForTest(elements);

        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Theory]
    [InlineData("[[1]]")]
    [InlineData("[]")]
    [InlineData("[1,2,3,4]")]
    [InlineData("1e400")]
    [InlineData("[1e400]")]
    public void CreatePartitionKeyFromArgument_RejectsUnsupportedComponents(string value)
    {
        Assert.Throws<JsonException>(() => TestCosmosCommand.CreatePartitionKeyFromArgumentForTest(value));
    }

    [Fact]
    public void CreatePartitionKeyFromArgument_EmptyObjectCreatesNone()
    {
        Assert.Equal(
            PartitionKey.None.ToString(),
            TestCosmosCommand.CreatePartitionKeyFromArgumentForTest("{}").ToString());
    }

    [Fact]
    public void CreatePartitionKeyFromArgument_EmptyObjectComponentsCreateHierarchicalNone()
    {
        var expected = new PartitionKeyBuilder()
            .Add("tenant")
            .AddNoneType()
            .Build();

        Assert.Equal(
            expected.ToString(),
            TestCosmosCommand.CreatePartitionKeyFromArgumentForTest("""["tenant",{}]""").ToString());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("undefined")]
    public void CreatePartitionKeyFromArgument_SentinelLikeWordsRemainStrings(string value)
    {
        Assert.Equal(
            new PartitionKey(value).ToString(),
            TestCosmosCommand.CreatePartitionKeyFromArgumentForTest(value).ToString());
    }

    [Fact]
    public void TryCreatePartitionKeyFromItem_MissingComponentsCreateNone()
    {
        using var item = JsonDocument.Parse("""{"tenant":"tenant-1"}""");
        var expected = new PartitionKeyBuilder()
            .Add("tenant-1")
            .AddNoneType()
            .Build();

        var created = TestCosmosCommand.TryCreatePartitionKeyFromItemForTest(
            item.RootElement,
            ["tenant", "user"],
            out var hasUndefinedComponent,
            out var partitionKey);

        Assert.True(created);
        Assert.True(hasUndefinedComponent);
        Assert.Equal(expected.ToString(), partitionKey.ToString());
    }

    [Theory]
    [InlineData("command-print-error-invalid_pk_json")]
    [InlineData("command-patch-error-invalid_pk_json")]
    [InlineData("command-rm-error-invalid_pk_json")]
    [InlineData("command-batch-error-invalid_pk_json")]
    [InlineData("command-sproc-error-invalid_pk")]
    public void InvalidPartitionKeyMessage_ExplainsSupportedComponents(string key)
    {
        var message = MessageService.GetString(key);

        Assert.Contains("JSON scalar", message);
        Assert.Contains("one to three", message);
        Assert.Contains("string, finite number, boolean, null, or {} components", message);
        Assert.Contains("Non-empty objects and nested arrays are not supported", message);
    }

    private sealed class TestCosmosCommand : CosmosCommand
    {
        private readonly double? requestCharge;
        private readonly double scopedRequestCharge;
        private readonly bool throwAfterRecording;
        private readonly bool cancelAfterRecording;

        public TestCosmosCommand(
            double? requestCharge = null,
            double scopedRequestCharge = 0,
            bool throwAfterRecording = false,
            bool cancelAfterRecording = false)
        {
            this.requestCharge = requestCharge;
            this.scopedRequestCharge = scopedRequestCharge;
            this.throwAfterRecording = throwAfterRecording;
            this.cancelAfterRecording = cancelAfterRecording;
        }

        public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
        {
            RequestChargeContext.Record(this.scopedRequestCharge);
            if (this.throwAfterRecording)
            {
                throw new InvalidOperationException("test");
            }

            if (this.cancelAfterRecording)
            {
                throw new OperationCanceledException();
            }

            commandState.RequestCharge = this.requestCharge;
            return Task.FromResult(commandState);
        }

        public static PartitionKey CreatePartitionKeyForTest(IReadOnlyList<JsonElement> elements)
        {
            return CreatePartitionKey(elements);
        }

        public static PartitionKey CreatePartitionKeyFromArgumentForTest(string value)
        {
            return CreatePartitionKeyFromArgument(value);
        }

        public static bool TryCreatePartitionKeyFromItemForTest(
            JsonElement item,
            IReadOnlyList<string> paths,
            out bool hasUndefinedComponent,
            out PartitionKey partitionKey)
        {
            return TryCreatePartitionKeyFromItem(item, paths, out hasUndefinedComponent, out partitionKey);
        }
    }
}
