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
