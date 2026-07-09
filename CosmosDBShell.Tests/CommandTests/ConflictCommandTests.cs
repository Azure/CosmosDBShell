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
/// Offline unit tests for <see cref="ConflictCommand"/>. These cover the not-connected,
/// wrong-scope, and argument-validation branches that execute before any network call,
/// plus the pure mode-normalization helper.
/// </summary>
public class ConflictCommandTests
{
    [Fact]
    public async Task Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new ConflictCommand { Subcommand = "show" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict show", CancellationToken.None));
    }

    [Fact]
    public async Task Connected_NoContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new ConflictCommand { Subcommand = "show" };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict show", CancellationToken.None));
    }

    [Fact]
    public async Task Database_NoContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "show" };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict show", CancellationToken.None));
    }

    [Fact]
    public async Task Connected_EmptyDatabaseAndContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new ConflictCommand { Subcommand = "show", Database = string.Empty, Container = string.Empty };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict show --database \"\" --container \"\"", CancellationToken.None));
    }

    [Fact]
    public async Task Database_EmptyContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "show", Container = string.Empty };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict show --container \"\"", CancellationToken.None));
    }

    [Fact]
    public async Task InvalidSubcommand_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "bogus" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict bogus", CancellationToken.None));
        Assert.Equal(
            MessageService.GetArgsString("command-conflict-error-invalid_subcommand", "subcommand", "bogus"),
            ex.Message);
    }

    [Fact]
    public async Task Set_NoArguments_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "set" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict set", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-conflict-error-missing_set_args"), ex.Message);
    }

    [Fact]
    public async Task Set_EmptyPathOnly_ThrowsMissingSetArgs()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "set", Path = string.Empty };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict set --path \"\"", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-conflict-error-missing_set_args"), ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Set_EmptyProcedureOnly_ThrowsMissingSetArgs(string procedure)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "set", Procedure = procedure };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict set --procedure \"\"", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-conflict-error-missing_set_args"), ex.Message);
    }

    [Fact]
    public async Task Set_LastWriterWinsWithEmptyProcedure_DoesNotThrowProcedureWithLww()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "set", Mode = "lastWriterWins", Procedure = string.Empty, Path = "/region" };

        // An empty --procedure must be treated as "not provided", so pre-validation must
        // not raise procedure_with_lww. The command instead proceeds to the network call,
        // which fails offline with a different error.
        var ex = await Record.ExceptionAsync(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict set --mode lastWriterWins --path /region --procedure \"\"", CancellationToken.None));
        Assert.False(
            ex is CommandException ce && ce.Message == MessageService.GetString("command-conflict-error-procedure_with_lww"),
            "Empty --procedure should not trigger procedure_with_lww.");
    }

    [Fact]
    public async Task Set_InvalidMode_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "set", Mode = "bogus" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict set --mode bogus", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-conflict-error-invalid_mode"), ex.Message);
    }

    [Fact]
    public async Task Set_CustomWithPath_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "set", Mode = "custom", Path = "/_ts" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict set --mode custom --path /_ts", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-conflict-error-path_with_custom"), ex.Message);
    }

    [Fact]
    public async Task Set_LastWriterWinsWithProcedure_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new ConflictCommand { Subcommand = "set", Mode = "lastWriterWins", Procedure = "resolve" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "conflict set --mode lastWriterWins --procedure resolve", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-conflict-error-procedure_with_lww"), ex.Message);
    }

    [Theory]
    [InlineData("lastWriterWins", "lastWriterWins")]
    [InlineData("lastwriterwins", "lastWriterWins")]
    [InlineData("lww", "lastWriterWins")]
    [InlineData("last-writer-wins", "lastWriterWins")]
    [InlineData("custom", "custom")]
    [InlineData("CUSTOM", "custom")]
    public void NormalizeMode_ReturnsCanonicalMode(string input, string expected)
    {
        Assert.Equal(expected, ConflictCommand.NormalizeMode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeMode_EmptyReturnsNull(string? input)
    {
        Assert.Null(ConflictCommand.NormalizeMode(input));
    }

    [Fact]
    public void NormalizeMode_InvalidThrowsCommandException()
    {
        var ex = Assert.Throws<CommandException>(() => ConflictCommand.NormalizeMode("bogus"));
        Assert.Equal(MessageService.GetString("command-conflict-error-invalid_mode"), ex.Message);
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
