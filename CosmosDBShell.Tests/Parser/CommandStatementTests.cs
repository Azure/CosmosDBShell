// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

public class CommandStatementTests
{
    private static Statement ParseStatement(string input)
    {
        var parser = new StatementParser(input);
        var statements = parser.ParseStatements();
        Assert.Single(statements);
        return statements[0];
    }

    // New helper that exposes parse errors (lexer collects them instead of throwing)
    private static (List<Statement> Statements, IReadOnlyList<ParseError> Errors) ParseWithErrors(string input)
    {
        var lexer = new Lexer(input);
        var parser = new StatementParser(lexer);
        var statements = parser.ParseStatements();
        return (statements, lexer.Errors);
    }

    [Fact]
    public void ParseCommandStatement_SimpleCommand_ParsesCorrectly()
    {
        var statement = ParseStatement("help");
        Assert.IsType<CommandStatement>(statement);
        var cmd = (CommandStatement)statement;
        Assert.Equal("help", cmd.Name);
        Assert.Empty(cmd.Arguments);
    }

    [Fact]
    public void ParseCommandStatement_CommandWithArguments_ParsesCorrectly()
    {
        var statement = ParseStatement("connect \"AccountEndpoint=https://localhost:8081/;AccountKey=key\"");
        var cmd = (CommandStatement)statement;
        Assert.Equal("connect", cmd.Name);
        Assert.Single(cmd.Arguments);
    }

    [Fact]
    public void ParseCommandStatement_CommandWithMultipleArguments_ParsesCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c\" myContainer");
        var cmd = (CommandStatement)statement;
        Assert.Equal("query", cmd.Name);
        Assert.Equal(2, cmd.Arguments.Count);
    }

    [Fact]
    public void ParseCommandStatement_CommandWithVariableArgument_ParsesCorrectly()
    {
        var statement = ParseStatement("echo $message");
        var cmd = (CommandStatement)statement;
        Assert.Equal("echo", cmd.Name);
        Assert.Single(cmd.Arguments);
        var expr = Assert.IsType<VariableExpression>(cmd.Arguments[0]);
        Assert.Equal("message", expr.Name);
    }

    [Fact]
    public void ParseCommandStatement_CommandWithSlash()
    {
        var statement = ParseStatement("foo /bar");
        var cmd = (CommandStatement)statement;
        Assert.Equal("foo", cmd.Name);
        Assert.Single(cmd.Arguments);
        var expr = Assert.IsType<ConstantExpression>(cmd.Arguments[0]);
        var identifier = Assert.IsType<ShellIdentifier>(expr.Value);
        Assert.Equal("/bar", identifier.Value);
    }

    [Fact]
    public void ParseCommandStatement_CommandWithExpressionArgument_ParsesCorrectly()
    {
        var statement = ParseStatement("calculate (2 + 3)");
        var cmd = (CommandStatement)statement;
        Assert.Equal("calculate", cmd.Name);
        Assert.Single(cmd.Arguments);

        // The parentheses create a ParensExpression wrapper
        var parens = Assert.IsType<ParensExpression>(cmd.Arguments[0]);

        // Inside the parentheses is the binary expression
        Assert.IsType<BinaryOperatorExpression>(parens.InnerExpression);
    }

    [Fact]
    public void ParseCommandStatement_CommandWithArrayArgument_ParsesCorrectly()
    {
        var statement = ParseStatement("process [1, 2, 3, 4, 5]");
        var cmd = (CommandStatement)statement;
        Assert.Equal("process", cmd.Name);
        Assert.Single(cmd.Arguments);
        Assert.IsType<JsonArrayExpression>(cmd.Arguments[0]);
    }

    [Fact]
    public async Task CommandStatement_UnknownCommand_ThrowsCommandNotFoundException()
    {
        var shell = ShellInterpreter.Instance;
        var commandState = new CommandState();
        var statement = ParseStatement("unknowncommand arg1 arg2");

        var ex = await Assert.ThrowsAsync<CommandNotFoundException>(
            async () => await statement.RunAsync(shell, commandState, CancellationToken.None));

        Assert.Contains("unknowncommand", ex.Message);
    }

    [Fact]
    public void CommandStatement_ToString_ReconstructsCommand()
    {
        var statement = ParseStatement("query \"SELECT * FROM c\" collection1");
        var cmd = (CommandStatement)statement;
        var result = cmd.ToString();
        Assert.Contains("query", result);
        Assert.Contains("SELECT * FROM c", result);
        Assert.Contains("collection1", result);
    }

    [Fact]
    public async Task CommandStatement_FunctionCall_ExecutesFunction()
    {
        var shell = ShellInterpreter.Instance;
        var commandState = new CommandState();
        var funcBody = ParseStatement("echo \"Function called\"");
        var defStatement = new DefStatement(
            new Token(TokenType.Identifier, "def", 0, 3),
            new Token(TokenType.Identifier, "myFunc", 0, 6),
            Array.Empty<string>(),
            funcBody);
        await defStatement.RunAsync(shell, commandState, CancellationToken.None);

        var statement = ParseStatement("myFunc");
        var result = await statement.RunAsync(shell, commandState, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public void ParseCommandStatement_PipeSymbol_CreatesPipeStatement()
    {
        var parser = new StatementParser("list | grep test");
        var statements = parser.ParseStatements();
        Assert.Single(statements);
        var pipeStatement = Assert.IsType<PipeStatement>(statements[0]);
        Assert.Equal(2, pipeStatement.Statements.Count);
        Assert.Equal("list", ((CommandStatement)pipeStatement.Statements[0]).Name);
        Assert.Equal("grep", ((CommandStatement)pipeStatement.Statements[1]).Name);
        Assert.Single(((CommandStatement)pipeStatement.Statements[1]).Arguments);
    }

    [Fact]
    public void ParseCommandStatement_ComplexExpression_ParsesAsArgument()
    {
        var statement = ParseStatement("calculate ((10 + 5) * 2)");
        var cmd = (CommandStatement)statement;
        Assert.Equal("calculate", cmd.Name);
        Assert.Single(cmd.Arguments);

        // The outer parentheses create a ParensExpression
        var outerParens = Assert.IsType<ParensExpression>(cmd.Arguments[0]);

        // Inside is the multiplication expression
        var multiplyExpr = Assert.IsType<BinaryOperatorExpression>(outerParens.InnerExpression);
        Assert.Equal(TokenType.Multiply, multiplyExpr.Operator);

        // The left side of multiplication is another ParensExpression (10 + 5)
        var leftParens = Assert.IsType<ParensExpression>(multiplyExpr.Left);
        var addExpr = Assert.IsType<BinaryOperatorExpression>(leftParens.InnerExpression);
        Assert.Equal(TokenType.Plus, addExpr.Operator);
    }

    [Fact]
    public void ParseCommandStatement_BooleanArguments_ParsesCorrectly()
    {
        var statement = ParseStatement("test true false");
        var cmd = (CommandStatement)statement;
        Assert.Equal("test", cmd.Name);
        Assert.Equal(2, cmd.Arguments.Count);
        Assert.True(((ShellBool)((ConstantExpression)cmd.Arguments[0]).Value).Value);
        Assert.False(((ShellBool)((ConstantExpression)cmd.Arguments[1]).Value).Value);
    }

    [Fact]
    public void ParseCommandStatement_SimpleOption()
    {
        var statement = ParseStatement("help -full");
        var cmd = (CommandStatement)statement;
        Assert.Equal("help", cmd.Name);
        var option = Assert.IsType<CommandOption>(cmd.Arguments.Single());
        Assert.Equal("full", option.Name);
    }

    [Fact]
    public void ParseCommandStatement_SimpleOptionMinusMinus()
    {
        var statement = ParseStatement("help --full");
        var cmd = (CommandStatement)statement;
        var option = Assert.IsType<CommandOption>(cmd.Arguments.Single());
        Assert.Equal("full", option.Name);
    }

    [Fact]
    public void ParseCommandStatement_SimpleOptionWithValue()
    {
        var statement = ParseStatement("help -full:true");
        var cmd = (CommandStatement)statement;
        var option = Assert.IsType<CommandOption>(cmd.Arguments.Single());
        Assert.Equal("full", option.Name);
        Assert.Equal("true", option.Value?.ToString());
    }

    [Fact]
    public void ParseCommandStatement_OptionWithEqualsVariableValue_ParsesVariableExpression()
    {
        var statement = ParseStatement("connect --mode=$foo");
        var cmd = (CommandStatement)statement;
        var option = Assert.IsType<CommandOption>(cmd.Arguments.Single());
        Assert.Equal("mode", option.Name);

        var variable = Assert.IsType<VariableExpression>(option.Value);
        Assert.Equal("foo", variable.Name);
    }

    [Fact]
    public void ParseCommandStatement_OutputRedirection_ParsesCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c\" out> results.json");
        var cmd = (CommandStatement)statement;
        Assert.Equal("query", cmd.Name);
        Assert.NotNull(cmd.OutRedirectToken);
        Assert.Equal("results.json", cmd.OutputRedirect);
    }

    [Fact]
    public void ParseCommandStatement_ErrorRedirection_ParsesCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c\" err> errors.log");
        var cmd = (CommandStatement)statement;
        Assert.NotNull(cmd.ErrRedirectToken);
        Assert.Equal("errors.log", cmd.ErrorRedirect);
    }

    [Fact]
    public void ParseCommandStatement_BothRedirections_ParsesCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c\" out> results.json err> errors.log");
        var cmd = (CommandStatement)statement;
        Assert.Equal("results.json", cmd.OutputRedirect);
        Assert.Equal("errors.log", cmd.ErrorRedirect);
    }

    [Fact]
    public void ParseCommandStatement_RedirectionsInReverseOrder_ParsesCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c\" err> errors.log out> results.json");
        var cmd = (CommandStatement)statement;
        Assert.Equal("errors.log", cmd.ErrorRedirect);
        Assert.Equal("results.json", cmd.OutputRedirect);
    }

    [Fact]
    public void ParseCommandStatement_OutputRedirectionWithQuotedFilename_ParsesCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c\" out> \"output file.json\"");
        var cmd = (CommandStatement)statement;
        Assert.Equal("output file.json", cmd.OutputRedirect);
    }

    [Fact]
    public void ParseCommandStatement_RedirectionWithOptions_ParsesCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c\" -max:10 out> results.json");
        var cmd = (CommandStatement)statement;
        Assert.Equal("query", cmd.Name);
        Assert.Equal("10", cmd.Arguments.OfType<CommandOption>().First().Value?.ToString());
        Assert.Equal("results.json", cmd.OutputRedirect);
    }

    [Fact]
    public void ParseCommandStatement_MultipleArgumentsWithRedirection_ParsesCorrectly()
    {
        var statement = ParseStatement("process file1.txt file2.txt file3.txt out> output.log err> error.log");
        var cmd = (CommandStatement)statement;
        Assert.Equal(3, cmd.Arguments.Count);
        Assert.Equal("output.log", cmd.OutputRedirect);
        Assert.Equal("error.log", cmd.ErrorRedirect);
    }

    [Fact]
    public void ParseCommandStatement_RedirectionBeforePipe_ParsesCorrectly()
    {
        var parser = new StatementParser("query \"SELECT * FROM c\" out> temp.json | filter name");
        var statements = parser.ParseStatements();
        var pipe = Assert.IsType<PipeStatement>(statements.Single());
        var firstCmd = (CommandStatement)pipe.Statements[0];
        Assert.Equal("temp.json", firstCmd.OutputRedirect);
    }

    // Adjusted tests below: previously used Assert.Throws, now check error list.

    [Fact]
    public void ParseCommandStatement_EmptyCommand_WithRedirection_ReportsError()
    {
        var (_, errors) = ParseWithErrors("out> file.txt");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ParseCommandStatement_RedirectionOnly_ReportsError()
    {
        var (_, errors) = ParseWithErrors("out> output.txt");
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("command out>")]
    [InlineData("command err>")]
    [InlineData("command out> out.txt out> out2.txt")]
    [InlineData("command err> err.txt err> err2.txt")]
    public void ParseCommandStatement_InvalidRedirection_ReportsErrors(string input)
    {
        var (_, errors) = ParseWithErrors(input);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ParseCommandStatement_ComplexScenario_ParsesCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c WHERE id > 5\" --format:json -max:100 out> \"results/output.json\" err> \"logs/errors.txt\"");
        var cmd = (CommandStatement)statement;
        Assert.Equal("query", cmd.Name);

        var nonOptionArgs = cmd.Arguments.Where(a => a is not CommandOption).ToList();
        Assert.Single(nonOptionArgs);

        var options = cmd.Arguments.OfType<CommandOption>().ToList();
        Assert.Equal(2, options.Count);

        Assert.Equal("json", options.First(o => o.Name == "format").Value?.ToString());
        Assert.Equal("100", options.First(o => o.Name == "max").Value?.ToString());

        Assert.Equal("results/output.json", cmd.OutputRedirect);
        Assert.False(cmd.AppendOutput);
        Assert.Equal("logs/errors.txt", cmd.ErrorRedirect);
        Assert.False(cmd.AppendError);
    }

    [Fact]
    public void ParseCommandStatement_ComplexScenario_ParsesAppendCorrectly()
    {
        var statement = ParseStatement("query \"SELECT * FROM c WHERE id > 5\" --format:json -max:100 out>> \"results/output.json\" err>> \"logs/errors.txt\"");
        var cmd = (CommandStatement)statement;
        Assert.Equal("query", cmd.Name);

        var options = cmd.Arguments.OfType<CommandOption>().ToList();
        Assert.Equal("json", options.First(o => o.Name == "format").Value?.ToString());
        Assert.Equal("100", options.First(o => o.Name == "max").Value?.ToString());

        Assert.Equal("results/output.json", cmd.OutputRedirect);
        Assert.True(cmd.AppendOutput);
        Assert.Equal("logs/errors.txt", cmd.ErrorRedirect);
        Assert.True(cmd.AppendError);
    }

    [Fact]
    public void ParseCommandStatement_Error()
    {
        var (statements, errors) = ParseWithErrors("help}");

        // Should have parsed "help" as a command
        Assert.Single(statements);
        var cmd = Assert.IsType<CommandStatement>(statements[0]);
        Assert.Equal("help", cmd.Name);
        Assert.Empty(cmd.Arguments);

        // Should have reported an error for the unexpected }
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("}") || e.Message.Contains("unexpected"));
    }

    [Fact]
    public void LockError_defFunc_Error()
    {
        var (statements, errors) = ParseWithErrors("def m[yFunc() {\r\no   echo \"Hello from myFunc\"\r\n}\r\nmyFunc");

        // Should have reported an error for the unexpected }
        Assert.NotEmpty(errors);
    }

}