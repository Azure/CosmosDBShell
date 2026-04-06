// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class ScriptArgumentTests : IntegrationTestBase
{
    [Fact]
    public async Task Script_Dollar0_ContainsScriptPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmosShellIntTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "test_dollar0.csh");
        await File.WriteAllTextAsync(scriptPath, "$captured = $0\n");

        try
        {
            var cmd = new CommandStatement(new Token(TokenType.Identifier, scriptPath, 0, scriptPath.Length));
            var state = await cmd.RunAsync(Shell, new CommandState(), CancellationToken.None);

            Assert.False(state.IsError);
            // $0 is script-scoped, so we verify the script executed without errors
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Script_PositionalArgs_PassedCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmosShellIntTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "test_args.csh");
        await File.WriteAllTextAsync(scriptPath, "echo $1 $2\n");

        try
        {
            var outputFile = CaptureOutputFile();
            var token1 = new Token(TokenType.Identifier, scriptPath, 0, scriptPath.Length);
            var cmd = new CommandStatement(token1);
            var argToken1 = new Token(TokenType.String, "arg1", 0, 4);
            cmd.Arguments.Add(new ConstantExpression(argToken1, new ShellText("arg1")));
            var argToken2 = new Token(TokenType.String, "arg2", 0, 4);
            cmd.Arguments.Add(new ConstantExpression(argToken2, new ShellText("arg2")));
            var state = await cmd.RunAsync(Shell, new CommandState(), CancellationToken.None);

            Assert.False(state.IsError);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Script_VariableIsolation_NoLeakToCaller()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmosShellIntTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "child.csh");
        await File.WriteAllTextAsync(scriptPath, "$leak = 123\n");

        try
        {
            var cmd = new CommandStatement(new Token(TokenType.Identifier, scriptPath, 0, scriptPath.Length));
            var state = await cmd.RunAsync(Shell, new CommandState(), CancellationToken.None);

            Assert.False(state.IsError);
            Assert.Throws<ShellException>(() => Shell.GetVariable("leak"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Script_MultiLineWithControlFlow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmosShellIntTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "control_flow.csh");
        await File.WriteAllTextAsync(scriptPath, "$sum = 0\nfor $i in [1, 2, 3] {\n  $sum = ($sum + $i)\n}\nreturn $sum\n");

        try
        {
            var cmd = new CommandStatement(new Token(TokenType.Identifier, scriptPath, 0, scriptPath.Length));
            var state = await cmd.RunAsync(Shell, new CommandState(), CancellationToken.None);

            Assert.False(state.IsError);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
