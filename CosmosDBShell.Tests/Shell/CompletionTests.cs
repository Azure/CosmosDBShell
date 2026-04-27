// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System.Collections.Concurrent;
using System.Reflection;
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

    [Fact]
    public void ClearDatabases_RemovesQueuedDatabaseRefresh()
    {
        var refreshTasks = GetRefreshTasks("DatabaseRefreshTasks");
        refreshTasks.Clear();
        refreshTasks["https://localhost:8081/"] = 1;

        CosmosCompleteCommand.ClearDatabases();

        Assert.Empty(refreshTasks);
    }

    [Fact]
    public void SetDatabases_RemovesQueuedDatabaseRefreshForClient()
    {
        CosmosCompleteCommand.ClearDatabases();
        var refreshTasks = GetRefreshTasks("DatabaseRefreshTasks");
        using var client = CreateTestClient();
        refreshTasks[client.Endpoint.ToString()] = 1;

        CosmosCompleteCommand.SetDatabases(client, ["ProductsDb"]);

        Assert.False(refreshTasks.ContainsKey(client.Endpoint.ToString()));
    }

    [Fact]
    public void ClearContainers_RemovesQueuedContainerRefresh()
    {
        var refreshTasks = GetRefreshTasks("ContainerRefreshTasks");
        refreshTasks.Clear();
        refreshTasks["https://localhost:8081/|ProductsDb"] = 1;

        CosmosCompleteCommand.ClearContainers();

        Assert.Empty(refreshTasks);
    }

    [Fact]
    public void SetContainers_RemovesQueuedContainerRefreshForDatabase()
    {
        CosmosCompleteCommand.ClearContainers();
        var refreshTasks = GetRefreshTasks("ContainerRefreshTasks");
        using var client = CreateTestClient();
        var key = string.Join('|', client.Endpoint.ToString(), "ProductsDb");
        refreshTasks[key] = 1;

        CosmosCompleteCommand.SetContainers(client, "ProductsDb", ["Orders"]);

        Assert.False(refreshTasks.ContainsKey(key));
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }

    private static ConcurrentDictionary<string, long> GetRefreshTasks(string fieldName)
    {
        var field = typeof(CosmosCompleteCommand).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var refreshTasks = Assert.IsType<ConcurrentDictionary<string, long>>(field.GetValue(null));
        return refreshTasks;
    }
}
