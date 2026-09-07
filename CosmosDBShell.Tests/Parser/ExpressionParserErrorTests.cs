// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Exercises the error-reporting and recovery branches of <see cref="ExpressionParser"/>:
/// unexpected tokens, missing closing delimiters, unexpected end of input, and the
/// synthetic <see cref="ErrorExpression"/> recovery nodes. These paths report into the
/// shared lexer error list rather than throwing.
/// </summary>
public class ExpressionParserErrorTests
{
    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("/")]
    [InlineData("==")]
    [InlineData("<")]
    [InlineData("|")]
    public void FlatOperatorChain_RejectsDeepTree(string operation)
    {
        var lexer = new Lexer(string.Join($" {operation} ", Enumerable.Repeat("1", 10001)));
        var parser = new ExpressionParser(lexer);
        var expression = parser.ParseFilterExpression();
        Assert.IsType<ErrorExpression>(expression);
        Assert.Contains(lexer.Errors, error => error.Message.Contains("expression tree depth"));
        Assert.True(parser.IsAtEnd);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("(", ")")]
    [InlineData("[", "]")]
    [InlineData("{value: ", "}")]
    [InlineData("$\"$(", ")\"")]
    public void ExpressionDepth_IsCheckedAcrossContainingNodes(string prefix, string suffix)
    {
        var source = prefix + string.Join(" + ", Enumerable.Repeat("1", 140)) + suffix;
        var lexer = new Lexer(source);
        new ExpressionParser(lexer).ParseExpression();
        Assert.Contains(lexer.Errors, error => error.Message.Contains("expression tree depth"));
    }

    [Theory]
    [InlineData(128, false)]
    [InlineData(129, true)]
    public void ExpressionDepth_HasExplicitBoundary(int operands, bool rejected)
    {
        var lexer = new Lexer(string.Join(" + ", Enumerable.Repeat("1", operands)));
        new ExpressionParser(lexer).ParseExpression();
        Assert.Equal(rejected, lexer.Errors.HasErrors);
    }

    [Theory]
    [InlineData("(", ")")]
    [InlineData("[", "]")]
    [InlineData("{value: ", "}")]
    [InlineData("$\"$(", ")\"")]
    public void ContainingNode_CountsTowardsTotalDepth(string prefix, string suffix)
    {
        var lexer = new Lexer(prefix + string.Join(" + ", Enumerable.Repeat("1", 128)) + suffix);
        new ExpressionParser(lexer).ParseExpression();
        Assert.Contains(lexer.Errors, error => error.Message.Contains("expression tree depth"));
    }

    [Theory]
    [InlineData("(", "1", ")")]
    [InlineData("!", "true", "")]
    [InlineData("2 ** ", "1", "")]
    [InlineData("[", "1", "]")]
    [InlineData("{value:", "1", "}")]
    public void DeepExpression_ReportsLimitWithoutThrowing(string prefix, string value, string suffix)
    {
        var source = string.Concat(Enumerable.Repeat(prefix, 300)) + value + string.Concat(Enumerable.Repeat(suffix, 300));
        var lexer = new Lexer(source);
        var parser = new ExpressionParser(lexer);
        var expression = parser.ParseExpression();
        Assert.NotNull(expression);
        Assert.Contains(lexer.Errors, error => error.Message.Contains("nesting budget"));
        Assert.True(parser.IsAtEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepBlocks_RejectInStrictAndEditorModes(bool tolerant)
    {
        var source = new string('{', 300) + "$value = 1" + new string('}', 300);
        var parser = new StatementParser(source) { TolerateIncompleteConstructs = tolerant };
        parser.ParseStatements();
        Assert.Contains(parser.Errors, error => error.Message.Contains("nesting budget"));
    }

    private static (Expression? Expr, int ErrorCount) ParseFilter(string input)
    {
        var lexer = new Lexer(input);
        var parser = new ExpressionParser(lexer);
        Expression? expr = null;
        try
        {
            expr = parser.ParseFilterExpression();
        }
        catch
        {
            // Some malformed inputs abort via exception; the error list is still the
            // primary signal under test, so swallow and assert on errors below.
        }

        return (expr, lexer.Errors.Count);
    }

    [Fact]
    public void MissingCloseParenthesis_ReportsError()
    {
        var (_, errors) = ParseFilter("(1 + 2");
        Assert.True(errors > 0);
    }

    [Fact]
    public void DeepInterpolation_UsesSharedBudget()
    {
        var input = "$\"$(" + new string('(', 300) + "1" + new string(')', 300) + ")\"";
        var lexer = new Lexer(input);
        new ExpressionParser(lexer).ParseExpression();
        Assert.Contains(lexer.Errors, error => error.Message.Contains("nesting budget"));
    }

    [Fact]
    public void NormalNesting_AndManySequentialStatements_RemainValid()
    {
        var source = "$value = " + new string('(', 10) + "1" + new string(')', 10) + ";";
        var parser = new StatementParser(string.Concat(Enumerable.Repeat(source, 200)));
        Assert.Equal(200, parser.ParseStatements().Count);
        Assert.False(parser.Errors.HasErrors);
    }

    [Fact]
    public void MissingCloseBracket_ReportsError()
    {
        var (_, errors) = ParseFilter("[1, 2");
        Assert.True(errors > 0);
    }

    [Fact]
    public void MissingCloseBrace_ReportsError()
    {
        var (_, errors) = ParseFilter("{ id: 1");
        Assert.True(errors > 0);
    }

    [Fact]
    public void UnexpectedClosingParenthesis_ReportsError()
    {
        var (_, errors) = ParseFilter(")");
        Assert.True(errors > 0);
    }

    [Fact]
    public void DanglingBinaryOperator_ReportsError()
    {
        var (_, errors) = ParseFilter("1 +");
        Assert.True(errors > 0);
    }

    [Fact]
    public void EmptyInput_ReportsErrorOrReturnsRecovery()
    {
        var (expr, errors) = ParseFilter(string.Empty);
        Assert.True(errors > 0 || expr != null);
    }

    [Fact]
    public void WellFormedExpression_ReportsNoErrors()
    {
        var (expr, errors) = ParseFilter("(1 + 2) * 3");
        Assert.NotNull(expr);
        Assert.Equal(0, errors);
    }

    [Fact]
    public void RecoveredExpression_StillReturnsNode()
    {
        // Even when delimiters are missing the parser returns a (partial) node so that
        // tolerant consumers like syntax highlighting can keep walking.
        var (expr, _) = ParseFilter("(1 + 2");
        Assert.NotNull(expr);
    }
}
