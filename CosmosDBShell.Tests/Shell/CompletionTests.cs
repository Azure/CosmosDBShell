// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using RadLine;

public class CompletionTests
{
    [Fact]
    public void TestCompleteKnown()
    {
        var completion = CosmosCompleteCommand.GetCompletion(ShellInterpreter.Instance, "he", AutoComplete.Next);
        Assert.Equal("help", completion);
    }

    [Fact]
    public void TestUnknownCommand()
    {
        var completion = CosmosCompleteCommand.GetCompletion(ShellInterpreter.Instance, "evlevlevlevlelv", AutoComplete.Next);
        Assert.Null(completion);
    }

    [Fact]
    public void CompleteDatabase_UsesCachedNames()
    {
        CosmosCompleteCommand.ClearDatabases();
        CosmosCompleteCommand.ClearContainers();

        using var shell = ShellInterpreter.CreateInstance();
        var client = CreateTestClient();
        shell.State = new ConnectedState(client);
        CosmosCompleteCommand.SetDatabases(client, ["ProductsDb"]);

        var completion = CosmosCompleteCommand.GetCompletion(shell, "cd Pro", AutoComplete.Next);

        Assert.Equal("cd ProductsDb", completion);
    }

    [Fact]
    public void CompleteContainer_UsesCachedNames()
    {
        CosmosCompleteCommand.ClearDatabases();
        CosmosCompleteCommand.ClearContainers();

        using var shell = ShellInterpreter.CreateInstance();
        var client = CreateTestClient();
        shell.State = new DatabaseState("ProductsDb", client);
        CosmosCompleteCommand.SetContainers(client, "ProductsDb", ["Orders"]);

        var completion = CosmosCompleteCommand.GetCompletion(shell, "cd Ord", AutoComplete.Next);

        Assert.Equal("cd Orders", completion);
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
