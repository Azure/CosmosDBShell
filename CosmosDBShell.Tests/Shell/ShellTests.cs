// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;

public class ShellTests
{
    [Fact]
    public async Task TestHelp()
    {
        var state = await ShellInterpreter.Instance.ExecuteCommandAsync("help", default);
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task TestCommandHelp()
    {
        foreach (var cmd in ShellInterpreter.Instance.App.Commands.Values)
        {
            var state = await ShellInterpreter.Instance.ExecuteCommandAsync("help " + cmd.CommandName, default);
            Assert.False(state.IsError, "Help for cmd '" + cmd.CommandName + "' failed.");
        }
    }

    [Fact]
    public async Task TestUnknownCommandHelp()
    {
        var state = await ShellInterpreter.Instance.ExecuteCommandAsync("help unknown", default);
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task TestUnknownCommand()
    {
        var state = await ShellInterpreter.Instance.ExecuteCommandAsync("foo_bar_baz", default);
        Assert.True(state.IsError);
    }


}
