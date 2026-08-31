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

        public TestCosmosCommand(double? requestCharge = null)
        {
            this.requestCharge = requestCharge;
        }

        public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
        {
            commandState.RequestCharge = this.requestCharge;
            return Task.FromResult(commandState);
        }

        public static PartitionKey CreatePartitionKeyForTest(IReadOnlyList<JsonElement> elements)
        {
            return CreatePartitionKey(elements);
        }
    }
}
