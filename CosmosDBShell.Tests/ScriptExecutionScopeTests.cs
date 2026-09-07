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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidScript_DoesNotRegisterFunctions_AndRestoresScope(bool expression)
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.SetVariable("value", new ShellNumber(1));
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "def mustNotRegister { return 1 }; if true {", TestContext.Current.CancellationToken);
            var token = new Token(TokenType.Identifier, path, 0, path.Length);
            var state = expression
                ? await new CommandExpression(token).RunScriptAsync(shell, new(), CancellationToken.None)
                : await new CommandStatement(token).RunScriptAsync(shell, new(), CancellationToken.None);
            Assert.True(state.IsError);
            Assert.False(shell.Functions.ContainsKey("mustNotRegister"));
            Assert.Single(shell.VariableContainers);
            Assert.Equal(1, Assert.IsType<ShellNumber>(shell.GetVariable("value")).Value);
            Assert.Null(shell.CurrentScriptFileName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExpressionScript_DoesNotModifyCaller_AndRestoresFrame()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.SetVariable("value", new ShellNumber(1));
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "$value = 2", TestContext.Current.CancellationToken);
            var expression = new CommandExpression(new(TokenType.Identifier, path, 0, path.Length));
            var state = await expression.RunScriptAsync(shell, new(), CancellationToken.None);
            Assert.False(state.IsError);
            Assert.Single(shell.VariableContainers);
            Assert.Equal(1, Assert.IsType<ShellNumber>(shell.GetVariable("value")).Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidExpressionScript_PropagatesError()
    {
        var shell = ShellInterpreter.CreateInstance();
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "if true {", TestContext.Current.CancellationToken);
            var expression = new CommandExpression(new(TokenType.Identifier, path, 0, path.Length));
            await Assert.ThrowsAsync<CommandException>(() => expression.EvaluateAsync(shell, new(), CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

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

    [Fact]
    public async Task RunScriptAsync_PrintFailure_ReturnsError()
    {
        var shell = ShellInterpreter.CreateInstance();
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmosShellTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "print.csh");
        await File.WriteAllTextAsync(scriptPath, "echo value\n", CancellationToken.None);

        var command = new CommandStatement(new Token(TokenType.Identifier, scriptPath, 0, scriptPath.Length))
        {
            OutRedirectToken = new Token(TokenType.RedirectOutput, ">", scriptPath.Length + 1, 1),
            OutRedirectDestToken = new Token(
                TokenType.String,
                Path.Combine(tempDir, "missing", "out.txt"),
                scriptPath.Length + 3,
                7),
        };

        var state = await command.RunAsync(shell, new CommandState(), CancellationToken.None);

        Assert.True(state.IsError);
    }
}
