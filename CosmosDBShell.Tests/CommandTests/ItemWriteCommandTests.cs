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
/// Offline unit tests for the item write commands (<see cref="PrintCommand"/>,
/// <see cref="ReplaceCommand"/>, <see cref="PatchCommand"/>). These cover the
/// argument validation and not-connected branches that execute before any network call.
/// </summary>
public class ItemWriteCommandTests
{
    [Fact]
    public async Task Print_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new PrintCommand { Id = "item-1", PartitionKey = "pk-1" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "print item-1 pk-1", CancellationToken.None));
    }

    [Fact]
    public async Task Replace_NoInputData_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new ReplaceCommand();

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "replace", CancellationToken.None));
        Assert.Equal(MessageService.GetString("error-no_input_data"), ex.Message);
    }

    [Fact]
    public async Task Replace_WithData_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new ReplaceCommand { Data = "{\"id\":\"1\"}" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "replace '{\"id\":\"1\"}'", CancellationToken.None));
    }

    [Fact]
    public async Task Patch_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new PatchCommand { Op = "set", Id = "1", Key = "1", Path = "/a", Value = "b" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "patch set 1 1 /a b", CancellationToken.None));
    }

    [Fact]
    public async Task Patch_MissingId_ThrowsCommandException()
    {
        var command = new PatchCommand { Op = "set", Id = null, Key = "1", Path = "/a", Value = "b" };

        var ex = await ExecutePatchInDatabaseAsync(command);
        Assert.Equal(MessageService.GetString("command-patch-error-missing_id"), ex.Message);
    }

    [Fact]
    public async Task Patch_MissingKey_ThrowsCommandException()
    {
        var command = new PatchCommand { Op = "set", Id = "1", Key = null, Path = "/a", Value = "b" };

        var ex = await ExecutePatchInDatabaseAsync(command);
        Assert.Equal(MessageService.GetString("command-patch-error-missing_pk"), ex.Message);
    }

    [Fact]
    public async Task Patch_MissingOp_ThrowsCommandException()
    {
        var command = new PatchCommand { Op = null, Id = "1", Key = "1", Path = "/a", Value = "b" };

        var ex = await ExecutePatchInDatabaseAsync(command);
        Assert.Equal(MessageService.GetString("command-patch-error-missing_op"), ex.Message);
    }

    [Fact]
    public async Task Patch_UnsupportedOp_ThrowsCommandException()
    {
        var command = new PatchCommand { Op = "frobnicate", Id = "1", Key = "1", Path = "/a", Value = "b" };

        var ex = await ExecutePatchInDatabaseAsync(command);
        Assert.Contains("frobnicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_InvalidPath_ThrowsCommandException()
    {
        var command = new PatchCommand { Op = "set", Id = "1", Key = "1", Path = "no-leading-slash", Value = "b" };

        var ex = await ExecutePatchInDatabaseAsync(command);
        Assert.Equal(MessageService.GetString("command-patch-error-invalid_path"), ex.Message);
    }

    private static async Task<CommandException> ExecutePatchInDatabaseAsync(PatchCommand command)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());

        return await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "patch", CancellationToken.None));
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
