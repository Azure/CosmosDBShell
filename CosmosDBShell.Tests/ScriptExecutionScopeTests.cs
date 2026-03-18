// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

public class ScriptExecutionScopeTests
{
    [Fact]
    public async Task RunScriptAsync_CreatesIsolatedFrame_ForNewVariables()
    {
        var shell = ShellInterpreter.CreateInstance();

        var tempDir = Path.Combine(Path.GetTempPath(), "CosmosShellTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var childScript = Path.Combine(tempDir, "child.csh");
        await File.WriteAllTextAsync(childScript, "$leak = 123\n", CancellationToken.None);

        var cmd = new CommandStatement(new Token(TokenType.Identifier, childScript, 0, childScript.Length));
        var state = await cmd.RunAsync(shell, new CommandState(), CancellationToken.None);
        Assert.False(state.IsError);

        // Variable defined inside the script should not leak into caller scope.
        Assert.Throws<ShellException>(() => shell.GetVariable("leak"));
    }

    [Fact]
    public async Task RunScriptAsync_DoesNotModifyExistingVariables_InCallerScope()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.SetVariable("x", new ShellNumber(1));

        var tempDir = Path.Combine(Path.GetTempPath(), "CosmosShellTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var childScript = Path.Combine(tempDir, "child.csh");

        // Script assigns to an existing variable name.
        await File.WriteAllTextAsync(childScript, "$x = 999\n", CancellationToken.None);

        var cmd = new CommandStatement(new Token(TokenType.Identifier, childScript, 0, childScript.Length));
        var state = await cmd.RunAsync(shell, new CommandState(), CancellationToken.None);
        Assert.False(state.IsError);

        // Caller scope variable must retain its original value.
        var x = shell.GetVariable("x");
        var n = Assert.IsType<ShellNumber>(x);
        Assert.Equal(1, n.Value);
    }
}
