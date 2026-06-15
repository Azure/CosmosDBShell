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
/// Unit tests for <see cref="DeleteCommand"/>. Covers the offline routing branches:
/// the invalid item-type error and the not-connected paths of the routed commands.
/// </summary>
public class DeleteCommandTests
{
    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData("itemz")]
    public async Task ExecuteAsync_InvalidItemType_ThrowsCommandException(string item)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new DeleteCommand { Item = item, Pattern = "x" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), $"delete {item} x", CancellationToken.None));
        Assert.Equal(MessageService.GetString("command-delete-error-invalid_item_type"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Database_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new DeleteCommand { Item = "database", Pattern = "MyDb" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "delete database MyDb", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Container_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new DeleteCommand { Item = "container", Pattern = "MyContainer" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "delete container MyContainer", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Item_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new DeleteCommand { Item = "item", Pattern = "test-*" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "delete item test-*", CancellationToken.None));
    }

    [Fact]
    public void CreateCommand_ItemTypeClassifiers_MatchExpected()
    {
        Assert.True(CreateCommand.IsItem("ITEM"));
        Assert.True(CreateCommand.IsItem("I"));
        Assert.True(CreateCommand.IsContainer("CONTAINER"));
        Assert.True(CreateCommand.IsContainer("C"));
        Assert.True(CreateCommand.IsDatabase("DATABASE"));
        Assert.True(CreateCommand.IsDatabase("DB"));
        Assert.False(CreateCommand.IsItem("CONTAINER"));
        Assert.False(CreateCommand.IsDatabase("ITEM"));
    }
}
