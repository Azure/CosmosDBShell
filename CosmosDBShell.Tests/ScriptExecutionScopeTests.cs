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
    [InlineData("if $cancel {}")]
    [InlineData("{ if $cancel {} }")]
    [InlineData("while true { if $cancel {} }")]
    [InlineData("do { if $cancel {} } while true")]
    [InlineData("for $item in [1] { if $cancel {} }")]
    [InlineData("loop { if $cancel {} }")]
    [InlineData("def run { if $cancel {} }; run")]
    [InlineData("def run { if $cancel {} }; $result = (run)")]
    [InlineData("def run { if $cancel {} }; exec \"run\"")]
    public async Task CancellationDuringFileExecution_IsNotReportedAsRuntimeFailure(string body)
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var cancellation = new CancellationTokenSource();
        var trigger = new CancelOnConversion(cancellation);
        var globals = new VariableContainer();
        globals.Set("cancel", trigger);
        shell.VariableContainers.Push(globals);
        var script = Path.GetTempFileName().Replace('\\', '/');
        var output = Path.GetTempFileName();
        var log = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(script, body, TestContext.Current.CancellationToken);
            shell.EnableDiagnostics(log);
            shell.ErrOutRedirect = output;
            var state = await shell.ExecuteCommandAsync($"exec \"{script}\"", cancellation.Token);

            Assert.True(trigger.WasEvaluated);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.False(state.IsError);
            Assert.Equal(ShellExitCode.Success, state.ExitCode);
            Assert.Single(shell.VariableContainers);
            Assert.Same(trigger, shell.GetVariable("cancel"));
            Assert.Null(shell.CurrentScriptFileName);
            Assert.Null(shell.CurrentScriptContent);
            Assert.Empty(File.ReadAllText(output));
            shell.Diagnostics!.Dispose();
            var entries = File.ReadAllText(log);
            Assert.Contains("[CANCELLED]", entries);
            Assert.DoesNotContain("[ERROR", entries);
            Assert.DoesNotContain("[FAIL]", entries);
            var next = await shell.RunCommandAsync(new(), "$value = 1", CancellationToken.None);
            Assert.False(next.IsError);
        }
        finally
        {
            shell.ErrOutRedirect = null;
            shell.Dispose();
            File.Delete(script);
            File.Delete(output);
            File.Delete(log);
        }
    }

    [Fact]
    public async Task RecursiveScript_StopsAtCallLimit_AndRestoresScope()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.SetVariable("value", new ShellNumber(1));
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "exec $0", TestContext.Current.CancellationToken);
            var command = new CommandStatement(new(TokenType.Identifier, path, 0, path.Length));
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => command.RunScriptAsync(shell, new(), CancellationToken.None));
            Assert.Contains("call depth", exception.ToString());
            Assert.Single(shell.VariableContainers);
            Assert.Null(shell.CurrentScriptFileName);
            var state = await shell.RunCommandAsync(new(), "$value = 2", CancellationToken.None);
            Assert.False(state.IsError);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
            var failure = await Assert.ThrowsAsync<CommandState.FailureException>(() => expression.EvaluateAsync(shell, new(), CancellationToken.None));
            var state = Assert.IsType<ParserErrorCommandState>(failure.State);
            Assert.Equal(path, state.SourceName);
            Assert.Equal("if true {", state.SourceText);
            Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(failure));
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
    private sealed class CancelOnConversion(CancellationTokenSource cancellation) : ShellObject(DataType.Boolean)
    {
        public bool WasEvaluated { get; private set; }

        public override object? ConvertShellObject(DataType type)
        {
            if (type != DataType.Boolean)
            {
                return new ShellBool(true).ConvertShellObject(type);
            }

            this.WasEvaluated = true;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }
    }
}
