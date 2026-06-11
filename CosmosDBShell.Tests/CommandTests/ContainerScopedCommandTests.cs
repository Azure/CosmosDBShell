// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Offline unit tests for <see cref="IndexPolicyCommand"/>, <see cref="RmContainerCommand"/>,
/// and <see cref="EditCommand"/>. These cover the not-connected and wrong-scope branches
/// that execute before any network or external-process call.
/// </summary>
public class ContainerScopedCommandTests
{
    [Fact]
    public async Task IndexPolicy_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new IndexPolicyCommand();

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "indexpolicy", CancellationToken.None));
    }

    [Fact]
    public async Task IndexPolicy_Connected_NoTarget_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new IndexPolicyCommand();

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "indexpolicy", CancellationToken.None));
    }

    [Fact]
    public async Task IndexPolicy_InDatabase_NoContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new IndexPolicyCommand();

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "indexpolicy", CancellationToken.None));
    }

    [Fact]
    public async Task RmContainer_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmContainerCommand { Name = "MyContainer", Force = true };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rmcon MyContainer true", CancellationToken.None));
    }

    [Fact]
    public async Task RmContainer_Connected_NoDatabase_ThrowsNotInDatabase()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new RmContainerCommand { Name = "MyContainer", Force = true };

        await Assert.ThrowsAsync<NotInDatabaseException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rmcon MyContainer true", CancellationToken.None));
    }

    [Fact]
    public async Task RmContainer_InContainer_NoDatabaseOption_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new RmContainerCommand { Name = "MyContainer", Force = true };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rmcon MyContainer true", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Edit_MissingPath_ThrowsCommandException(string? path)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new EditCommand { FilePath = path };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "edit", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-edit-missing-path"), ex.Message);
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
