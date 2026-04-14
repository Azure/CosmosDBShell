// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

public class PwdCommandTests
{
    [Fact]
    public async Task PwdCommand_WhenDisconnected_ReturnsNotConnected()
    {
        var shell = ShellInterpreter.CreateInstance();

        var state = await shell.ExecuteCommandAsync("pwd", CancellationToken.None);

        Assert.False(state.IsError);
        Assert.True(state.IsPrinted);
        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.Equal(ShellLocation.NotConnectedText, result.Value.GetProperty("currentLocation").GetString());
    }

    [Fact]
    public async Task PwdCommand_WhenConnectedAtRoot_ReturnsRootLocation()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(null!);

        var state = await shell.ExecuteCommandAsync("pwd", CancellationToken.None);

        Assert.False(state.IsError);
        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.Equal("/", result.Value.GetProperty("currentLocation").GetString());
    }

    [Fact]
    public async Task PwdCommand_InDatabaseState_ReturnsDatabaseLocation()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", null!);

        var state = await shell.ExecuteCommandAsync("pwd", CancellationToken.None);

        Assert.False(state.IsError);
        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.Equal("/TestDatabase", result.Value.GetProperty("currentLocation").GetString());
    }

    [Fact]
    public async Task PwdCommand_InContainerState_ReturnsContainerLocation()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", null!);

        var state = await shell.ExecuteCommandAsync("pwd", CancellationToken.None);

        Assert.False(state.IsError);
        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.Equal("/TestDatabase/TestContainer", result.Value.GetProperty("currentLocation").GetString());
    }
}