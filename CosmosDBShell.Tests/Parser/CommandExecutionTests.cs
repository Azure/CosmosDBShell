// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Drives execution of <see cref="CommandStatement"/> and <see cref="ExecStatement"/>:
/// positional/variadic binding, option binding, the help short-circuit, dynamic
/// <c>exec</c> dispatch, and the error branches reached through <c>CreateCommandAsync</c>.
/// </summary>
public class CommandExecutionTests : TestBase
{
    private Statement ParseSingle(string script)
        => new StatementParser(script).ParseStatements().Single();

    private Task<CommandState> RunSingleAsync(string script)
        => ParseSingle(script).RunAsync(Shell, new CommandState(), CancellationToken.None);

    [Fact]
    public async Task Command_WithVariadicArguments_BindsAllPositional()
    {
        var state = await RunSingleAsync("echo hello world");
        Assert.Equal("hello world", Assert.IsType<ShellText>(state.Result).Text);
    }

    [Fact]
    public async Task Command_WithHelpOption_ShowsHelp()
    {
        var state = await RunSingleAsync("echo --help");
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Command_ValuedOptions_AreBoundBeforeExecution()
    {
        // settings binds --db / --con then fails on the missing connection.
        await Assert.ThrowsAsync<NotConnectedException>(
            () => RunSingleAsync("settings --db=mydb --con=mycon"));
    }

    [Fact]
    public async Task Command_UnknownOption_Throws()
    {
        await Assert.ThrowsAsync<UnknownOptionException>(
            () => RunSingleAsync("settings --bogus=1"));
    }

    [Fact]
    public async Task Command_OptionMissingValue_Throws()
    {
        await Assert.ThrowsAsync<CommandException>(
            () => RunSingleAsync("settings --db"));
    }

    [Fact]
    public async Task Command_TooManyPositionalArguments_Throws()
    {
        // print declares exactly two positional parameters (id, key).
        await Assert.ThrowsAsync<CommandException>(
            () => RunSingleAsync("print a b c"));
    }

    [Fact]
    public async Task Command_Unknown_Throws_CommandNotFoundException()
    {
        await Assert.ThrowsAsync<CommandNotFoundException>(
            () => RunSingleAsync("totallyunknowncommand999 arg"));
    }

    [Fact]
    public async Task Exec_StringLiteralCommand_RunsIt()
    {
        var state = await RunSingleAsync("exec \"echo\" hi there");
        Assert.Equal("hi there", Assert.IsType<ShellText>(state.Result).Text);
    }

    [Fact]
    public async Task Exec_VariableCommand_RunsResolvedCommand()
    {
        SetVariable("cmd", new ShellText("echo"));

        var state = await RunSingleAsync("exec $cmd hello world");

        Assert.Equal("hello world", Assert.IsType<ShellText>(state.Result).Text);
    }

    [Fact]
    public async Task Exec_EmptyCommandPath_Throws()
    {
        SetVariable("cmd", new ShellText(string.Empty));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunSingleAsync("exec $cmd"));
    }

    [Fact]
    public async Task Exec_FailingCommandWithScriptContext_WrapsInPositionalException()
    {
        // When the shell carries script context, errors raised by the dynamically
        // executed command are re-thrown as PositionalException with line/column info.
        Shell.CurrentScriptFileName = "script.csh";
        Shell.CurrentScriptContent = "exec $cmd";
        SetVariable("cmd", new ShellText("totallyunknowncommand999"));

        var ex = await Assert.ThrowsAsync<PositionalException>(
            () => RunSingleAsync("exec $cmd"));
        Assert.Equal("script.csh", ex.FileName);
    }
}
