// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Exercises <see cref="CommandExpression"/> evaluation paths: built-in command
/// execution, user-defined function dispatch, positional/variadic parameter binding,
/// option binding, and the error branches (unknown command, unknown option, missing
/// option value) reached via <c>CreateCommandAsync</c>.
/// </summary>
public class CommandExpressionTests : TestBase
{
    private async Task<ShellObject> EvalCommandAsync(string input)
    {
        var expr = new ExpressionParser(new Lexer(input)).ParseFilterExpression();
        return await expr.EvaluateAsync(Shell, new CommandState(), CancellationToken.None);
    }

    [Fact]
    public async Task BuiltinCommand_InExpression_ReturnsResult()
    {
        var result = await EvalCommandAsync("(echo hello)");
        var text = Assert.IsType<ShellText>(result);
        Assert.Equal("hello", text.Text);
    }

    [Fact]
    public async Task BuiltinCommand_WithMultipleArguments_BindsVariadicParameter()
    {
        var result = await EvalCommandAsync("(echo hello world)");
        var text = Assert.IsType<ShellText>(result);
        Assert.Equal("hello world", text.Text);
    }

    [Fact]
    public async Task UserDefinedFunction_InExpression_IsInvoked()
    {
        await RunScriptAsync("def greet() { return \"hi\" }");

        var result = await EvalCommandAsync("(greet)");

        var text = Assert.IsType<ShellText>(result);
        Assert.Equal("hi", text.Text);
    }

    [Fact]
    public async Task UserDefinedFunction_WithArgument_ReceivesArgument()
    {
        await RunScriptAsync("def echoback($who) { return $who }");

        var result = await EvalCommandAsync("(echoback world)");

        var text = Assert.IsType<ShellText>(result);
        Assert.Equal("world", text.Text);
    }

    [Fact]
    public async Task UnknownCommand_Throws_CommandNotFoundException()
    {
        await Assert.ThrowsAsync<CommandNotFoundException>(
            () => EvalCommandAsync("(definitelynotacommand123)"));
    }

    [Fact]
    public async Task UnknownOption_Throws_UnknownOptionException()
    {
        await Assert.ThrowsAsync<UnknownOptionException>(
            () => EvalCommandAsync("(info --bogus=1)"));
    }

    [Fact]
    public async Task ValuedOption_IsBound_ThenCommandExecutes()
    {
        // info binds --db / --con (exercising the valued-option branch) and then
        // fails because the shell is not connected. Reaching NotConnectedException
        // proves option binding completed successfully.
        await Assert.ThrowsAsync<NotConnectedException>(
            () => EvalCommandAsync("(info --db=mydb --con=mycon)"));
    }

    [Fact]
    public async Task NonBooleanOption_WithoutValue_Throws_CommandException()
    {
        await Assert.ThrowsAsync<CommandException>(
            () => EvalCommandAsync("(info --db)"));
    }

    [Fact]
    public async Task PositionalParameters_AreBound_ThenCommandExecutes()
    {
        // print binds two positional parameters (id, key) before failing on the
        // missing connection; reaching NotConnectedException proves binding succeeded.
        await Assert.ThrowsAsync<NotConnectedException>(
            () => EvalCommandAsync("(print a b)"));
    }

    [Fact]
    public async Task TooManyPositionalArguments_Throws_CommandException()
    {
        await Assert.ThrowsAsync<CommandException>(
            () => EvalCommandAsync("(print a b c)"));
    }

    [Fact]
    public async Task VariadicParameter_AbsorbsAllPositionalArguments()
    {
        // echo's variadic Messages parameter consumes every positional argument.
        var result = await EvalCommandAsync("(echo one two three)");
        Assert.Equal("one two three", Assert.IsType<ShellText>(result).Text);
    }
}
