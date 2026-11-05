// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Collections.Generic;
using System.Linq;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Covers the append-redirect synthesis (<c>&gt;&gt;</c>, <c>2&gt;&gt;</c>), duplicate and
/// missing-destination redirect error branches, option <c>:</c>/<c>=</c>/<c>--</c> value
/// syntax, and JSON-path variable parsing (<c>$x.name</c>, <c>$items[0]</c>, <c>$.name</c>)
/// in <see cref="StatementParser"/> / <see cref="ExpressionParser"/>.
/// </summary>
public class StatementParserStructureTests
{
    private static CommandStatement ParseCommand(string input)
    {
        var statement = new StatementParser(input).ParseStatements().Single();
        return Assert.IsType<CommandStatement>(statement);
    }

    private static (List<Statement> Statements, IReadOnlyList<ParseError> Errors) ParseWithErrors(string input)
    {
        var lexer = new Lexer(input);
        var parser = new StatementParser(lexer);
        return (parser.ParseStatements(), lexer.Errors);
    }

    [Theory]
    [InlineData("def duplicate [value value] { return $value }")]
    [InlineData("def duplicate [value $value] { return $value }")]
    public void DuplicateParameters_HighlightFunctionDefinition(string source)
    {
        var result = StatementParser.ScriptParseResult.Parse(source);

        var error = Assert.Single(result.Errors);
        Assert.Equal(0, error.Start);
        Assert.Equal(source.Length, error.Length);
    }

    [Fact]
    public void IndexPaths_PreserveQuestionMarksAndStatementBoundaries()
    {
        const string input = "index add /city/? /docType/?; index remove /city/?";
        var (statements, errors) = ParseWithErrors(input);

        Assert.Empty(errors);
        Assert.Equal(2, statements.Count);
        var add = Assert.IsType<CommandStatement>(statements[0]);
        Assert.Equal(new[] { "add", "/city/?", "/docType/?" }, add.Arguments.Select(argument => argument.ToString()));
        Assert.Equal(7, add.Arguments[1].Length);
        Assert.Equal(input.IndexOf("/city/?", StringComparison.Ordinal), add.Arguments[1].Start);
        var remove = Assert.IsType<CommandStatement>(statements[1]);
        Assert.Equal(new[] { "remove", "/city/?" }, remove.Arguments.Select(argument => argument.ToString()));
    }

    [Theory]
    [InlineData("index add /city/?")]
    [InlineData("exec \"index\" add /city/?")]
    [InlineData("$policy = (index add /city/?)")]
    [InlineData("index add --paths=/city/?")]
    public void IndexPaths_PreserveQuestionMarksAcrossCommandForms(string input)
    {
        var (statements, errors) = ParseWithErrors(input);

        Assert.Empty(errors);
        var statement = Assert.Single(statements);
        var arguments = statement switch
        {
            CommandStatement command => command.Arguments,
            ExecStatement executed => executed.Arguments,
            AssignmentStatement assignment => Assert.IsType<CommandExpression>(
                Assert.IsType<ParensExpression>(assignment.Value).InnerExpression).Arguments,
            _ => throw new InvalidOperationException("Unexpected statement type."),
        };
        var path = arguments[^1] is CommandOption option ? option.Value : arguments[^1];
        Assert.NotNull(path);
        Assert.Equal("/city/?", path.ToString());
    }

    [Fact]
    public void AppendOutputRedirect_IsRecognized()
    {
        var cmd = ParseCommand("query \"SELECT * FROM c\" >> results.json");
        Assert.True(cmd.AppendOutput);
        Assert.Equal("results.json", cmd.OutputRedirect);
    }

    [Fact]
    public void AppendErrorRedirect_IsRecognized()
    {
        var cmd = ParseCommand("query \"SELECT * FROM c\" 2>> errors.log");
        Assert.True(cmd.AppendError);
        Assert.Equal("errors.log", cmd.ErrorRedirect);
    }

    [Fact]
    public void DuplicateOutputRedirect_ReportsError()
    {
        var (_, errors) = ParseWithErrors("query \"q\" > a.json > b.json");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void DuplicateErrorRedirect_ReportsError()
    {
        var (_, errors) = ParseWithErrors("query \"q\" 2> a.log 2> b.log");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void OutputRedirect_MissingDestination_ReportsError()
    {
        var (_, errors) = ParseWithErrors("query \"q\" >");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Option_WithColonValue_ParsesValue()
    {
        var cmd = ParseCommand("query \"q\" -max:10");
        var option = cmd.Arguments.OfType<CommandOption>().Single();
        Assert.Equal("max", option.Name);
        Assert.Equal("10", option.Value?.ToString());
    }

    [Fact]
    public void Option_WithEqualsValue_ParsesValue()
    {
        var cmd = ParseCommand("query \"q\" --max=25");
        var option = cmd.Arguments.OfType<CommandOption>().Single();
        Assert.Equal("max", option.Name);
        Assert.Equal("25", option.Value?.ToString());
    }

    [Fact]
    public void Variable_WithPropertyAccess_ParsesAsJsonPath()
    {
        var expr = new ExpressionParser(new Lexer("$script.name")).ParseExpression();
        Assert.IsType<JSonPathExpression>(expr);
    }

    [Fact]
    public void Variable_WithArrayAccess_ParsesAsJsonPath()
    {
        var expr = new ExpressionParser(new Lexer("$items[0]")).ParseExpression();
        Assert.IsType<JSonPathExpression>(expr);
    }

    [Fact]
    public void PipedResultPath_ParsesAsJsonPath()
    {
        var expr = new ExpressionParser(new Lexer("$.name")).ParseExpression();
        Assert.IsType<JSonPathExpression>(expr);
    }

    [Fact]
    public void PlainVariable_ParsesAsVariableExpression()
    {
        var expr = new ExpressionParser(new Lexer("$foo")).ParseExpression();
        var variable = Assert.IsType<VariableExpression>(expr);
        Assert.Equal("foo", variable.Name);
    }
}
