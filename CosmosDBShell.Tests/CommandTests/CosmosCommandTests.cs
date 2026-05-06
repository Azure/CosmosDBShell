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
        public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
        {
            return Task.FromResult(commandState);
        }

        public static PartitionKey CreatePartitionKeyForTest(IReadOnlyList<JsonElement> elements)
        {
            return CreatePartitionKey(elements);
        }
    }
}
