// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Collections.Generic;

using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Covers structural parsing and error-recovery paths in <see cref="StatementParser"/>
/// that are not exercised by the happy-path statement tests: pipes, exec, function
/// parameter lists, tolerant partial parsing, and recoverable syntax errors.
/// </summary>
public class StatementParserEdgeTests
{
    private static List<Statement> Parse(string input)
    {
        return new StatementParser(input).ParseStatements();
    }

    private static (List<Statement> Statements, StatementParser Parser) ParseWithParser(string input)
    {
        var parser = new StatementParser(input);
        return (parser.ParseStatements(), parser);
    }

    [Fact]
    public void Pipe_TwoCommands_ProducesPipeStatement()
    {
        var statements = Parse("echo a | echo b");
        var pipe = Assert.IsType<PipeStatement>(Assert.Single(statements));
        Assert.Equal(2, pipe.Statements.Count);
    }

    [Fact]
    public void Pipe_ThreeCommands_ProducesPipeStatementWithThreeSegments()
    {
        var statements = Parse("echo a | echo b | echo c");
        var pipe = Assert.IsType<PipeStatement>(Assert.Single(statements));
        Assert.Equal(3, pipe.Statements.Count);
    }

    [Fact]
    public void Exec_WithArguments_ProducesExecStatement()
    {
        var statements = Parse("exec echo hi there");
        Assert.IsType<ExecStatement>(Assert.Single(statements));
    }

    [Theory]
    [InlineData("for $x typo [1,2] {}", "typo", "statement_error_expected_in")]
    [InlineData("do {} typo false", "typo", "statement_error_expected_while")]
    public void Loop_InvalidKeyword_ReportsErrorAtKeyword(string source, string keyword, string messageKey)
    {
        var (_, parser) = ParseWithParser(source);

        var error = Assert.Single(parser.Errors);
        Assert.Equal(source.IndexOf(keyword, StringComparison.Ordinal), error.Start);
        Assert.Equal(keyword.Length, error.Length);
        Assert.Equal(Azure.Data.Cosmos.Shell.Util.MessageService.GetString(messageKey), error.Message);
    }

    [Theory]
    [InlineData("for $x IN [1,2] {}")]
    [InlineData("do {} WHILE false")]
    public void Loop_KeywordsRemainCaseInsensitive(string source)
    {
        var (statements, parser) = ParseWithParser(source);

        Assert.Single(statements);
        Assert.Empty(parser.Errors);
    }

    [Theory]
    [InlineData("-directory . -r")]
    [InlineData("--directory=. --recursive")]
    [InlineData("--directory:. --recursive=false")]
    [InlineData("--directory=$path")]
    [InlineData("-5 -1.5 +2")]
    [InlineData("https://localhost:8081 *.json \"two words\"")]
    [InlineData("{\"id\":1} [1,2] true $value (1 + 2)")]
    public void Exec_ArgumentsMatchDirectCommandParsing(string arguments)
    {
        var (directStatements, directParser) = ParseWithParser($"dir {arguments}");
        var (execStatements, execParser) = ParseWithParser($"exec $cmd {arguments}");

        Assert.Empty(directParser.Errors);
        Assert.Empty(execParser.Errors);
        var direct = Assert.IsType<CommandStatement>(Assert.Single(directStatements));
        var exec = Assert.IsType<ExecStatement>(Assert.Single(execStatements));
        Assert.Equal(direct.Arguments.Count, exec.Arguments.Count);
        for (int i = 0; i < direct.Arguments.Count; i++)
        {
            Assert.Equal(direct.Arguments[i].GetType(), exec.Arguments[i].GetType());
            Assert.Equal(CommandArgumentFormatter.Format(direct.Arguments[i]), CommandArgumentFormatter.Format(exec.Arguments[i]));
        }
    }

    [Theory]
    [InlineData("exec \"echo\" --format=")]
    [InlineData("exec \"echo\" --format=; echo after")]
    public void Exec_MissingInlineOptionValue_ReportsError(string source)
    {
        var (_, parser) = ParseWithParser(source);

        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void Def_WithParenParameters_ParsesParameters()
    {
        var statements = Parse("def add($a, $b) { return ($a + $b) }");
        var def = Assert.IsType<DefStatement>(Assert.Single(statements));
        Assert.Equal("add", def.Name);
        Assert.Equal(new[] { "a", "b" }, def.Parameters);
    }

    [Fact]
    public void Def_WithBracketParameters_ParsesParameters()
    {
        var statements = Parse("def greet [name] { echo $name }");
        var def = Assert.IsType<DefStatement>(Assert.Single(statements));
        Assert.Equal("greet", def.Name);
        Assert.Equal(new[] { "name" }, def.Parameters);
    }

    [Fact]
    public void Def_WithoutParameters_ParsesEmptyParameterList()
    {
        var statements = Parse("def now() { echo hi }");
        var def = Assert.IsType<DefStatement>(Assert.Single(statements));
        Assert.Empty(def.Parameters);
    }

    [Fact]
    public void MultipleStatements_SeparatedBySemicolons_AreAllParsed()
    {
        var statements = Parse("$x = 1; $y = 2; $z = 3");
        Assert.Equal(3, statements.Count);
        Assert.All(statements, s => Assert.IsType<AssignmentStatement>(s));
    }

    [Fact]
    public void Comment_IsRecorded_AndDoesNotBecomeStatement()
    {
        var (statements, parser) = ParseWithParser("echo a # trailing comment\necho b");
        Assert.Equal(2, statements.Count);
        Assert.NotEmpty(parser.Comments);
    }

    [Fact]
    public void EmptyInput_ProducesNoStatements()
    {
        Assert.Empty(Parse("   \n  \t  "));
    }

    [Fact]
    public void UnexpectedCloseBrace_ReportsErrorAndDoesNotThrow()
    {
        var (_, parser) = ParseWithParser("}");
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void IncompleteBlock_WithoutToleration_ReportsErrorAndDropsStatement()
    {
        var (statements, parser) = ParseWithParser("{ echo a");
        Assert.Empty(statements);
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void IncompleteBlock_WithToleration_ReturnsPartialBlock()
    {
        var parser = new StatementParser("{ echo a")
        {
            TolerateIncompleteConstructs = true,
        };

        var statements = parser.ParseStatements();

        var block = Assert.IsType<BlockStatement>(Assert.Single(statements));
        Assert.Single(block.Statements);
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void Pipe_WithMissingRightHandSide_ReportsError()
    {
        var (_, parser) = ParseWithParser("echo a |");
        Assert.NotEmpty(parser.Errors);
    }
}
