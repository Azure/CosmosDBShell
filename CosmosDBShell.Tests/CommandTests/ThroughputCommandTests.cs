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
/// Offline unit tests for <see cref="ThroughputCommand"/>. These cover the not-connected,
/// wrong-scope, and argument-validation branches that execute before any network call.
/// </summary>
public class ThroughputCommandTests
{
    [Fact]
    public async Task Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new ThroughputCommand { Subcommand = "show" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "throughput show", CancellationToken.None));
    }

    [Fact]
    public async Task Connected_NoDatabase_ThrowsNotInDatabase()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new ThroughputCommand { Subcommand = "show" };

        await Assert.ThrowsAsync<NotInDatabaseException>(
            () => command.ExecuteAsync(shell, new CommandState(), "throughput show", CancellationToken.None));
    }

    [Fact]
    public async Task InvalidSubcommand_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new ThroughputCommand { Subcommand = "bogus" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "throughput bogus", CancellationToken.None));
        Assert.Equal(
            MessageService.GetArgsString("command-throughput-error-invalid_subcommand", "subcommand", "bogus"),
            ex.Message);
    }

    [Theory]
    [InlineData("set")]
    [InlineData("manual")]
    [InlineData("autoscale")]
    public async Task Write_MissingRu_ThrowsCommandException(string subcommand)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new ThroughputCommand { Subcommand = subcommand };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), $"throughput {subcommand}", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-throughput-error-missing_ru"), ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task Write_NonPositiveRu_ThrowsCommandException(int ru)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new ThroughputCommand { Subcommand = "set", Ru = ru };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), $"throughput set {ru}", CancellationToken.None));
        Assert.Equal(
            MessageService.GetArgsString("command-throughput-error-invalid_ru", "ru", ru),
            ex.Message);
    }

    [Fact]
    public async Task Show_WithRu_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new ThroughputCommand { Subcommand = "show", Ru = 4000 };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "throughput show 4000", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-throughput-error-show_no_args"), ex.Message);
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
