// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Commands;
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
        // info binds --db / --con then fails on the missing connection.
        await Assert.ThrowsAsync<NotConnectedException>(
            () => RunSingleAsync("info --db=mydb --con=mycon"));
    }

    [Fact]
    public async Task Command_UnknownOption_Throws()
    {
        await Assert.ThrowsAsync<UnknownOptionException>(
            () => RunSingleAsync("info --bogus=1"));
    }

    [Fact]
    public async Task Command_OptionMissingValue_Throws()
    {
        await Assert.ThrowsAsync<CommandException>(
            () => RunSingleAsync("info --db"));
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

    [Theory]
    [InlineData("--text value --flag --max 5", "value", true)]
    [InlineData("-t value -f -m 5", "value", true)]
    [InlineData("--text=value --flag=true --max=5", "value", true)]
    [InlineData("--text:value --flag:false --max:5", "value", false)]
    [InlineData("--text=\"two words\" --flag --max=(2 + 3)", "two words", true)]
    [InlineData("--text=$text --flag --max=$max", "two words", true)]
    public async Task Exec_BindsOptionsLikeDirectCommands(string arguments, string expectedText, bool expectedFlag)
    {
        Assert.True(CommandFactory.TryCreateFactory(typeof(ExecBindingCommand), out var factory));
        Shell.App.Commands["execbinding"] = factory;
        SetVariable("cmd", new ShellText("execbinding"));
        SetVariable("text", new ShellText("two words"));
        SetVariable("max", new ShellNumber(5));
        try
        {
            var direct = await Shell.RunCommandAsync(new(), $"execbinding {arguments}", CancellationToken.None);
            var dynamic = await Shell.RunCommandAsync(new(), $"exec $cmd {arguments}", CancellationToken.None);

            Assert.False(direct.IsError);
            Assert.False(dynamic.IsError);
            var value = Assert.IsType<ShellJson>(dynamic.Result).Value;
            Assert.Equal(Assert.IsType<ShellJson>(direct.Result).Value.GetRawText(), value.GetRawText());
            Assert.Equal(expectedText, value.GetProperty("text").GetString());
            Assert.Equal(expectedFlag, value.GetProperty("flag").GetBoolean());
            Assert.Equal(5, value.GetProperty("max").GetInt32());
        }
        finally
        {
            Shell.App.Commands.Remove("execbinding");
        }
    }

    [Fact]
    public async Task Exec_HelpOption_ShowsHelp()
    {
        var state = await Shell.RunCommandAsync(new(), "exec \"echo\" --help", CancellationToken.None);
        Assert.False(state.IsError);
    }

    [Theory]
    [InlineData("--bogus=1", typeof(UnknownOptionException))]
    [InlineData("--db", typeof(CommandException))]
    public async Task Exec_InvalidOptions_ReportCommandErrors(string arguments, Type exceptionType)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            Shell.RunCommandAsync(new(), $"exec \"info\" {arguments}", CancellationToken.None));
        Assert.IsType(exceptionType, exception);
    }

    [Theory]
    [InlineData("exec \"identity\" --name=$text", "--name=two words")]
    [InlineData("identity --name:$text", "--name:two words")]
    [InlineData("$result = (identity --name=$text)", "--name=two words")]
    [InlineData("exec \"identity\" --help", "--help")]
    [InlineData("exec \"identity\" -m", "-m")]
    public async Task Function_OptionWords_ArePositionalText(string invocation, string expected)
    {
        SetVariable("text", new ShellText("two words"));
        var state = await Shell.RunCommandAsync(
            new(), $"def identity [value] {{ return $value }}; {invocation}", CancellationToken.None);

        Assert.False(state.IsError);
        var result = invocation.StartsWith("$result", StringComparison.Ordinal) ? GetVariable("result") : state.Result;
        Assert.Equal(expected, Assert.IsType<ShellText>(result).Text);
    }

    [Theory]
    [InlineData("exec")]
    [InlineData("command")]
    [InlineData("expression")]
    public async Task Script_PassesOptionWordsAndRestoresScope(string callKind)
    {
        var path = Path.GetTempFileName();
        SetVariable("cmd", new ShellText(path));
        SetVariable("text", new ShellText("two words"));
        var scopeCount = Shell.VariableContainers.Count;
        try
        {
            await File.WriteAllTextAsync(path, "return [$1, $2, $3]", TestContext.Current.CancellationToken);
            const string invocation = "exec $cmd --name=$text -m 5";
            var parsed = StatementParser.ScriptParseResult.Parse(invocation);
            Assert.False(parsed.Errors.HasErrors);
            var arguments = Assert.IsType<ExecStatement>(Assert.Single(parsed.Statements)).Arguments;
            var command = new CommandStatement(new Token(TokenType.Identifier, path, 0, path.Length));
            command.Arguments.AddRange(arguments);
            var expression = new CommandExpression(command.CommandToken);
            expression.Arguments.AddRange(arguments);
            var state = callKind switch
            {
                "command" => await command.RunAsync(Shell, new(), CancellationToken.None),
                "expression" => await expression.ExecuteCommandAsync(Shell, new(), CancellationToken.None),
                _ => await Shell.RunCommandAsync(new(), invocation, CancellationToken.None),
            };

            Assert.False(state.IsError);
            var values = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal("--name=two words", values[0].GetString());
            Assert.Equal("-m", values[1].GetString());
            Assert.Equal("5", values[2].GetString());
            Assert.Equal(scopeCount, Shell.VariableContainers.Count);
            Assert.Null(Shell.CurrentScriptFileName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Exec_ShellWords_PreservePathsAndNegativeArguments()
    {
        var state = await Shell.RunCommandAsync(new(), "exec \"echo\" C:\\temp\\file.txt https://example.test/a -5", CancellationToken.None);
        Assert.False(state.IsError);
        Assert.Equal("C:\\temp\\file.txt https://example.test/a -5", Assert.IsType<ShellText>(state.Result).Text);
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

[CosmosCommand("execbinding")]
internal class ExecBindingCommand : CosmosCommand
{
    [CosmosOption("text", "t")]
    public string? Text { get; init; }

    [CosmosOption("flag", "f")]
    public bool Flag { get; init; }

    [CosmosOption("max", "m")]
    public int? Max { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        commandState.Result = new ShellJson(System.Text.Json.JsonSerializer.SerializeToElement(new { text = this.Text, flag = this.Flag, max = this.Max }));
        return Task.FromResult(commandState);
    }
}
