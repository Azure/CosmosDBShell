// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Drives the error-wrapping branches in the loop statements: when a loop body throws
/// and the shell carries script context, the exception is re-thrown as a
/// <see cref="PositionalException"/>. Nested loops also exercise the
/// <c>catch (PositionalException)</c> re-throw branch.
/// </summary>
public class StatementPositionalErrorTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScriptFunctionFailure_RecordsEachCallerOnce(bool nestedScript)
    {
        var script = Path.GetTempFileName().Replace('\\', '/');
        var caller = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(script, "def broken {\n totallyunknowncmd999\n}\nbroken", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(caller, $"exec \"{script.Replace('\\', '/')}\"", TestContext.Current.CancellationToken);
            var path = nestedScript ? caller : script;
            var command = new CommandStatement(new Token(TokenType.Identifier, path, 0, path.Length));
            var exception = await Assert.ThrowsAsync<PositionalException>(() => command.RunScriptAsync(Shell, new(), TestContext.Current.CancellationToken));
            var frames = PositionalException.GetSourceTrace(exception);
            Assert.Single(frames, frame => frame.FileName == script && frame.Line == 2);
            Assert.Single(frames, frame => frame.FileName == script && frame.Line == 4);
            if (nestedScript)
            {
                Assert.Single(frames, frame => frame.FileName == caller && frame.Line == 1);
            }

            Assert.Equal(nestedScript ? 3 : 2, frames.Count);
        }
        finally
        {
            File.Delete(script);
            File.Delete(caller);
        }
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("$result = (broken)")]
    public async Task FunctionFailure_PreservesDefinitionAndCallerSources(string invocation)
    {
        const string definition = "def broken {\n totallyunknowncmd999\n}";
        Shell.CurrentScriptFileName = "definition.csh";
        Shell.CurrentScriptContent = definition;
        await new StatementParser(definition).ParseStatement()!.RunAsync(Shell, new(), CancellationToken.None);
        Shell.CurrentScriptFileName = "caller.csh";
        Shell.CurrentScriptContent = invocation;

        var exception = await Assert.ThrowsAsync<PositionalException>(() => new StatementParser(invocation).ParseStatement()!.RunAsync(Shell, new(), CancellationToken.None));
        var frames = PositionalException.GetSourceTrace(exception);
        Assert.Equal("definition.csh", frames[0].FileName);
        Assert.Equal(2, frames[0].Line);
        Assert.Equal("caller.csh", frames[^1].FileName);
        Assert.Equal("caller.csh", Shell.CurrentScriptFileName);
        Assert.Equal(invocation, Shell.CurrentScriptContent);
        Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(exception));
    }

    private async Task RunWithScriptContextAsync(string script)
    {
        Shell.CurrentScriptFileName = "script.csh";
        Shell.CurrentScriptContent = script;

        var statements = new StatementParser(script).ParseStatements();
        var state = new CommandState();
        foreach (var statement in statements)
        {
            state = await statement.RunAsync(Shell, state, CancellationToken.None);
        }
    }

    [Fact]
    public async Task While_BodyThrows_WithScriptContext_WrapsInPositionalException()
    {
        var ex = await Assert.ThrowsAsync<PositionalException>(
            () => RunWithScriptContextAsync("while true { totallyunknowncmd999 }"));
        Assert.Equal("script.csh", ex.FileName);
    }

    [Fact]
    public async Task DoWhile_BodyThrows_WithScriptContext_WrapsInPositionalException()
    {
        var ex = await Assert.ThrowsAsync<PositionalException>(
            () => RunWithScriptContextAsync("do { totallyunknowncmd999 } while false"));
        Assert.Equal("script.csh", ex.FileName);
    }

    [Fact]
    public async Task Loop_BodyThrows_WithScriptContext_WrapsInPositionalException()
    {
        var ex = await Assert.ThrowsAsync<PositionalException>(
            () => RunWithScriptContextAsync("loop { totallyunknowncmd999 }"));
        Assert.Equal("script.csh", ex.FileName);
    }

    [Fact]
    public async Task For_BodyThrows_WithScriptContext_WrapsInPositionalException()
    {
        var ex = await Assert.ThrowsAsync<PositionalException>(
            () => RunWithScriptContextAsync("for $x in [1] { totallyunknowncmd999 }"));
        Assert.Equal("script.csh", ex.FileName);
    }

    [Fact]
    public async Task NestedLoops_InnerWraps_OuterRethrowsPositionalException()
    {
        // The inner loop wraps the failure into a PositionalException; the outer loop's
        // catch (PositionalException) branch re-throws it unchanged.
        var ex = await Assert.ThrowsAsync<PositionalException>(
            () => RunWithScriptContextAsync(
                "for $x in [1] { for $y in [1] { totallyunknowncmd999 } }"));
        Assert.Equal("script.csh", ex.FileName);
    }
}
