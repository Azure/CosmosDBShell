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
/// Unit tests for <see cref="BucketCommand"/>. Covers the pure validation helper and
/// the offline state visitors that do not require a live Cosmos DB connection.
/// </summary>
public class BucketCommandTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void CheckBucket_WithinRange_ReturnsTrue(int bucket)
    {
        Assert.True(BucketCommand.CheckBucket(bucket));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(99)]
    public void CheckBucket_OutOfRange_ReturnsFalse(int bucket)
    {
        Assert.False(BucketCommand.CheckBucket(bucket));
    }

    [Fact]
    public async Task ExecuteAsync_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new BucketCommand { Action = "3" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket 3", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Connected_ThrowsNotInDatabase()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new BucketCommand { Action = "3" };

        await Assert.ThrowsAsync<NotInDatabaseException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket 3", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_SetsBucket()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "3" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "bucket 3", CancellationToken.None);

        Assert.False(state.IsError);
        Assert.Equal(3, client.ClientOptions.ThroughputBucket);
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ZeroResetsBucket()
    {
        var client = CreateTestClient();
        client.ClientOptions.ThroughputBucket = 4;
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "0" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "bucket 0", CancellationToken.None);

        Assert.False(state.IsError);
        Assert.Null(client.ClientOptions.ThroughputBucket);
    }

    [Fact]
    public async Task ExecuteAsync_InContainer_NoArgs_ShowsCurrent()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", client);
        var command = new BucketCommand();

        var state = await command.ExecuteAsync(shell, new CommandState(), "bucket", CancellationToken.None);

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_InvalidValue_ReturnsEmptyState()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "99" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "bucket 99", CancellationToken.None);

        Assert.False(state.IsError);
        Assert.Null(client.ClientOptions.ThroughputBucket);
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_InvalidSubcommand_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "bogus" };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket bogus", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ShowWithoutContainer_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "show" };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket show", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Connected_ServerActionWithoutDatabase_ThrowsNotInDatabase()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new BucketCommand { Action = "show" };

        await Assert.ThrowsAsync<NotInDatabaseException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket show", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ClientSelectionWithExtraArgs_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "3", Id = 1 };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket 3", CancellationToken.None));
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
