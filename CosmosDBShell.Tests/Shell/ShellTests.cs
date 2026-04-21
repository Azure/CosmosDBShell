// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        foreach (var cmd in ShellInterpreter.Instance.App.Commands.Values.DistinctBy(c => c.CommandName))
        {
            var state = await ShellInterpreter.Instance.ExecuteCommandAsync("help " + cmd.CommandName, TestContext.Current.CancellationToken);
            Assert.False(state.IsError, "Help for cmd '" + cmd.CommandName + "' failed.");
        }
    }

    [Fact]
    public async Task TestClearAlias()
    {
        var state = await ShellInterpreter.Instance.ExecuteCommandAsync("clear", TestContext.Current.CancellationToken);
        Assert.False(state.IsError);
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

        var informationalVersion = typeof(ShellInterpreter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            Assert.True(
                !actualVersion.Contains('+'),
                $"Expected version output to omit build metadata and match the display version contract. Actual version: '{actualVersion}'. Raw informational version: '{informationalVersion}'. Display version: '{expectedVersion}'.");
        }
    }

    [Fact]
    public async Task TestEchoRedirectWritesFile()
    {
        var file = $"cosmosshell-test-{Guid.NewGuid():N}.txt";
        try
        {
            var state = await ShellInterpreter.Instance.ExecuteCommandAsync(
                $"echo \"FooBar\" >{file}",
                TestContext.Current.CancellationToken);

            Assert.False(state.IsError);
            Assert.True(File.Exists(file), $"Expected redirected file to exist at {file}.");
            var content = File.ReadAllText(file).TrimEnd('\r', '\n');
            Assert.Equal("FooBar", content);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            ShellInterpreter.Instance.StdOutRedirect = null;
            ShellInterpreter.Instance.ErrOutRedirect = null;
        }
    }

    [Fact]
    public async Task TestEchoAppendRedirectWritesFile()
    {
        var file = $"cosmosshell-test-{Guid.NewGuid():N}.txt";
        try
        {
            var state1 = await ShellInterpreter.Instance.ExecuteCommandAsync(
                $"echo \"line1\" >{file}",
                TestContext.Current.CancellationToken);
            Assert.False(state1.IsError);

            var state2 = await ShellInterpreter.Instance.ExecuteCommandAsync(
                $"echo \"line2\" >>{file}",
                TestContext.Current.CancellationToken);
            Assert.False(state2.IsError);

            var content = File.ReadAllText(file);
            Assert.Contains("line1", content);
            Assert.Contains("line2", content);
            Assert.True(content.IndexOf("line1", StringComparison.Ordinal) < content.IndexOf("line2", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            ShellInterpreter.Instance.StdOutRedirect = null;
            ShellInterpreter.Instance.ErrOutRedirect = null;
        }
    }

}
