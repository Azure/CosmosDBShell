// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
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
    }
}
