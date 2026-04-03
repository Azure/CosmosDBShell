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
using Azure.Data.Cosmos.Shell.Parser;

public class ShellTests
{
    [Fact]
    public async Task TestHelp()
    {
        var state = await ShellInterpreter.Instance.ExecuteCommandAsync("help", TestContext.Current.CancellationToken);
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task TestCommandHelp()
    {
        foreach (var cmd in ShellInterpreter.Instance.App.Commands.Values)
        {
            var state = await ShellInterpreter.Instance.ExecuteCommandAsync("help " + cmd.CommandName, TestContext.Current.CancellationToken);
            Assert.False(state.IsError, "Help for cmd '" + cmd.CommandName + "' failed.");
        }
    }

    [Fact]
    public async Task TestUnknownCommandHelp()
    {
        var state = await ShellInterpreter.Instance.ExecuteCommandAsync("help unknown", TestContext.Current.CancellationToken);
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task TestUnknownCommand()
    {
        var state = await ShellInterpreter.Instance.ExecuteCommandAsync("foo_bar_baz", TestContext.Current.CancellationToken);
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task VersionCommand_UsesInformationalVersion()
    {
        var state = await ShellInterpreter.Instance.ExecuteCommandAsync("version", TestContext.Current.CancellationToken);

        Assert.False(state.IsError);
        Assert.True(state.IsPrinted);

        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.True(result.Value.TryGetProperty("version", out var versionProperty));

        var expectedVersion = ShellInterpreter.GetDisplayVersion(typeof(ShellInterpreter).Assembly);
        var actualVersion = versionProperty.GetString();

        Assert.NotNull(actualVersion);
        Assert.Equal(expectedVersion, actualVersion);
        Assert.Contains("+", actualVersion, StringComparison.Ordinal);
    }


}
