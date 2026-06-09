// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class RmCommandTests
{
    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }

    [Fact]
    public async Task ExecuteAsync_NoPatternAndNoPipeInput_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new RmCommand { Pattern = null };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm", TestContext.Current.CancellationToken));
        Assert.Equal(MessageService.GetString("command-rm-error-no_filter"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmCommand { Pattern = "test-*" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm test-*", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_ConnectedWithoutDatabaseAndContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new RmCommand { Pattern = "test-*" };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm test-*", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabaseWithoutContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDb", CreateTestClient());
        var command = new RmCommand { Pattern = "test-*" };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm test-*", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TryGetPartitionKeyElements_ReturnsAllHierarchicalValues()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenantId": "tenant-a",
          "userId": "user-b",
          "sessionId": "session-c"
        }
        """);

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenantId", "userId", "sessionId"],
            out var partitionKeyElements);

        Assert.True(found);
        Assert.Equal(["tenant-a", "user-b", "session-c"], partitionKeyElements.Select(element => element.GetString()!).ToArray());
    }

    [Fact]
    public void TryGetPartitionKeyElements_SupportsNestedHierarchicalValues()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenant": {
            "id": "tenant-a"
          },
          "user": {
            "id": "user-b"
          }
        }
        """);

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenant/id", "user/id"],
            out var partitionKeyElements);

        Assert.True(found);
        Assert.Equal(["tenant-a", "user-b"], partitionKeyElements.Select(element => element.GetString()!).ToArray());
    }

    [Fact]
    public void TryGetPartitionKeyElements_ReturnsFalseWhenAnyPathIsMissing()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenantId": "tenant-a"
        }
        """);

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenantId", "userId"],
            out var partitionKeyElements);

        Assert.False(found);
        Assert.Empty(partitionKeyElements);
    }
}
