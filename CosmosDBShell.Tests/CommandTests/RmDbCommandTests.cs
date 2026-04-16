// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class RmDbCommandTests
{
    [Fact]
    public void UpdateStateAfterDelete_CurrentDatabase_ResetsToConnectedState()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = CreateTestClient();
        shell.State = new DatabaseState("TestDatabase", client);

        RmDbCommand.UpdateStateAfterDelete(shell, client, "TestDatabase");

        Assert.IsType<ConnectedState>(shell.State);
    }

    [Fact]
    public void UpdateStateAfterDelete_DifferentDatabase_KeepsCurrentState()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = CreateTestClient();
        shell.State = new DatabaseState("CurrentDatabase", client);

        RmDbCommand.UpdateStateAfterDelete(shell, client, "OtherDatabase");

        var state = Assert.IsType<DatabaseState>(shell.State);
        Assert.Equal("CurrentDatabase", state.DatabaseName);
    }

    [Fact]
    public void RmDbLocalizationKeys_ArePresent()
    {
        var deletedDatabaseMessage = MessageService.GetString("command-rmdb-deleted_db", new Dictionary<string, object> { { "db", "TestDatabase" } });
        var confirmDeletionMessage = MessageService.GetString("command-rmdb-confirm_db_deletion");

        Assert.False(string.IsNullOrWhiteSpace(deletedDatabaseMessage));
        Assert.False(string.IsNullOrWhiteSpace(confirmDeletionMessage));
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}