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
/// Offline unit tests for <see cref="TtlCommand"/>. These cover the not-connected,
/// wrong-scope, and argument-validation branches that execute before any network call,
/// plus the pure status-mapping helper.
/// </summary>
public class TtlCommandTests
{
    [Fact]
    public async Task Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new TtlCommand { Subcommand = "show" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "ttl show", CancellationToken.None));
    }

    [Fact]
    public async Task Connected_NoContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new TtlCommand { Subcommand = "show" };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "ttl show", CancellationToken.None));
    }

    [Fact]
    public async Task Database_NoContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new TtlCommand { Subcommand = "show" };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "ttl show", CancellationToken.None));
    }

    [Fact]
    public async Task InvalidSubcommand_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new TtlCommand { Subcommand = "bogus" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "ttl bogus", CancellationToken.None));
        Assert.Equal(
            MessageService.GetArgsString("command-ttl-error-invalid_subcommand", "subcommand", "bogus"),
            ex.Message);
    }

    [Fact]
    public async Task Show_WithSeconds_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new TtlCommand { Subcommand = "show", Seconds = 3600 };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "ttl show 3600", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-ttl-error-show_no_args"), ex.Message);
    }

    [Fact]
    public async Task Set_MissingSeconds_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new TtlCommand { Subcommand = "set" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "ttl set", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-ttl-error-missing_seconds"), ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task Set_NonPositiveSeconds_ThrowsCommandException(int seconds)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new TtlCommand { Subcommand = "set", Seconds = seconds };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), $"ttl set {seconds}", CancellationToken.None));
        Assert.Equal(
            MessageService.GetArgsString("command-ttl-error-invalid_seconds", "seconds", seconds),
            ex.Message);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    public async Task Toggle_WithSeconds_ThrowsCommandException(string subcommand)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new TtlCommand { Subcommand = subcommand, Seconds = 3600 };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), $"ttl {subcommand} 3600", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-ttl-error-toggle_no_args"), ex.Message);
    }

    [Theory]
    [InlineData(null, "disabled")]
    [InlineData(-1, "no-default")]
    [InlineData(1, "enabled")]
    [InlineData(86400, "enabled")]
    public void StatusFor_MapsRawTtlToStatus(int? defaultTimeToLive, string expected)
    {
        Assert.Equal(expected, TtlCommand.StatusFor(defaultTimeToLive));
    }

    [Theory]
    [InlineData(null, "disabled")]
    [InlineData(0L, "disabled")]
    [InlineData(-1L, "enabled")]
    [InlineData(1L, "enabled")]
    [InlineData(2592000L, "enabled")]
    public void AnalyticalStatusFor_MapsRawTtlToStatus(long? analyticalTimeToLive, string expected)
    {
        Assert.Equal(expected, TtlCommand.AnalyticalStatusFor(analyticalTimeToLive));
    }

    [Fact]
    public async Task Analytical_Show_WithSeconds_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new TtlCommand { Subcommand = "show", Seconds = 3600, Analytical = true };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "ttl show 3600 --analytical", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-ttl-error-show_no_args"), ex.Message);
    }

    [Fact]
    public async Task Analytical_Set_MissingSeconds_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new TtlCommand { Subcommand = "set", Analytical = true };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "ttl set --analytical", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-ttl-error-missing_seconds"), ex.Message);
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
