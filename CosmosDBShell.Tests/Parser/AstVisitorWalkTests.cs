// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Collections.Generic;

using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Walks parsed ASTs with a concrete <see cref="AstVisitor"/> subclass to exercise the
/// default traversal behavior of the base visitor (token visits and child recursion).
/// </summary>
public class AstVisitorWalkTests
{
    private sealed class RecordingVisitor : AstVisitor
    {
        public HashSet<string> Visited { get; } = new();

        public int TokenCount { get; private set; }

        public override void VisitToken(Token token)
        {
            this.TokenCount++;
        }

        public override void Visit(ErrorExpression errorExpression)
        {
            this.Visited.Add(nameof(ErrorExpression));
            base.Visit(errorExpression);
        }

        public override void Visit(ConstantExpression constantExpression)
        {
            this.Visited.Add(nameof(ConstantExpression));
            base.Visit(constantExpression);
        }

        public override void Visit(UnaryOperatorExpression unaryOperatorExpression)
        {
            this.Visited.Add(nameof(UnaryOperatorExpression));
            base.Visit(unaryOperatorExpression);
        }

        public override void Visit(BinaryOperatorExpression binaryOperatorExpression)
        {
            this.Visited.Add(nameof(BinaryOperatorExpression));
            base.Visit(binaryOperatorExpression);
        }

        public override void Visit(FilterPipeExpression filterPipeExpression)
        {
            this.Visited.Add(nameof(FilterPipeExpression));
            base.Visit(filterPipeExpression);
        }

        public override void Visit(ParensExpression parensExpression)
        {
            this.Visited.Add(nameof(ParensExpression));
            base.Visit(parensExpression);
        }

        public override void Visit(JsonExpression jsonExpression)
        {
            this.Visited.Add(nameof(JsonExpression));
            base.Visit(jsonExpression);
        }

        public override void Visit(JsonArrayExpression jSonArrayExpression)
        {
            this.Visited.Add(nameof(JsonArrayExpression));
            base.Visit(jSonArrayExpression);
        }

        public override void Visit(JSonPathExpression jSonPathExpression)
        {
            this.Visited.Add(nameof(JSonPathExpression));
            base.Visit(jSonPathExpression);
        }

        public override void Visit(FilterPathExpression filterPathExpression)
        {
            this.Visited.Add(nameof(FilterPathExpression));
            base.Visit(filterPathExpression);
        }

        public override void Visit(FilterCallExpression filterCallExpression)
        {
            this.Visited.Add(nameof(FilterCallExpression));
            base.Visit(filterCallExpression);
        }

        public override void Visit(InterpolatedStringExpression interpolatedStringExpression)
        {
            this.Visited.Add(nameof(InterpolatedStringExpression));
            base.Visit(interpolatedStringExpression);
        }

        public override void Visit(VariableExpression variableExpression)
        {
            this.Visited.Add(nameof(VariableExpression));
            base.Visit(variableExpression);
        }

        public override void Visit(CommandExpression commandExpression)
        {
            this.Visited.Add(nameof(CommandExpression));
            base.Visit(commandExpression);
        }

        public override void Visit(CommandOption commandOption)
        {
            this.Visited.Add(nameof(CommandOption));
            base.Visit(commandOption);
        }

        public override void Visit(AssignmentStatement assignmentStatement)
        {
            this.Visited.Add(nameof(AssignmentStatement));
            base.Visit(assignmentStatement);
        }

        public override void Visit(BlockStatement blockStatement)
        {
            this.Visited.Add(nameof(BlockStatement));
            base.Visit(blockStatement);
        }

        public override void Visit(BreakStatement breakStatement)
        {
            this.Visited.Add(nameof(BreakStatement));
            base.Visit(breakStatement);
        }

        public override void Visit(CommandStatement commandStatement)
        {
            this.Visited.Add(nameof(CommandStatement));
            base.Visit(commandStatement);
        }

        public override void Visit(ContinueStatement continueStatement)
        {
            this.Visited.Add(nameof(ContinueStatement));
            base.Visit(continueStatement);
        }

        public override void Visit(DefStatement defStatement)
        {
            this.Visited.Add(nameof(DefStatement));
            base.Visit(defStatement);
        }

        public override void Visit(DoWhileStatement doWhileStatement)
        {
            this.Visited.Add(nameof(DoWhileStatement));
            base.Visit(doWhileStatement);
        }

        public override void Visit(ExecStatement execStatement)
        {
            this.Visited.Add(nameof(ExecStatement));
            base.Visit(execStatement);
        }

        public override void Visit(ForStatement forStatement)
        {
            this.Visited.Add(nameof(ForStatement));
            base.Visit(forStatement);
        }

        public override void Visit(IfStatement ifStatement)
        {
            this.Visited.Add(nameof(IfStatement));
            base.Visit(ifStatement);
        }

        public override void Visit(LoopStatement loopStatement)
        {
            this.Visited.Add(nameof(LoopStatement));
            base.Visit(loopStatement);
        }

        public override void Visit(PipeStatement pipeStatement)
        {
            this.Visited.Add(nameof(PipeStatement));
            base.Visit(pipeStatement);
        }

        public override void Visit(ReturnStatement returnStatement)
        {
            this.Visited.Add(nameof(ReturnStatement));
            base.Visit(returnStatement);
        }

        public override void Visit(WhileStatement whileStatement)
        {
            this.Visited.Add(nameof(WhileStatement));
            base.Visit(whileStatement);
        }
    }

    private static List<Statement> Parse(string script)
    {
        return new StatementParser(script).ParseStatements();
    }

    private static Expression ParseFilterExpr(string input)
    {
        return new ExpressionParser(new Lexer(input)).ParseFilterExpression();
    }

    private static void Accept(RecordingVisitor visitor, string script)
    {
        foreach (var statement in Parse(script))
        {
            statement.Accept(visitor);
        }
    }

    [Fact]
    public void Walk_AllStatementTypes_AreVisited()
    {
        var visitor = new RecordingVisitor();

        Accept(visitor, "$x = 1");
        Accept(visitor, "{ echo hi }");
        Accept(visitor, "break");
        Accept(visitor, "echo hello");
        Accept(visitor, "continue");
        Accept(visitor, "def f() { return 1 }");
        Accept(visitor, "do { break } while $x");
        Accept(visitor, "exec foo");
        Accept(visitor, "for $i in [1, 2] echo $i");
        Accept(visitor, "if $x { echo a } else { echo b }");
        Accept(visitor, "loop break");
        Accept(visitor, "echo a | echo b");
        Accept(visitor, "return 1");
        Accept(visitor, "while $x break");

        Assert.Contains(nameof(AssignmentStatement), visitor.Visited);
        Assert.Contains(nameof(BlockStatement), visitor.Visited);
        Assert.Contains(nameof(BreakStatement), visitor.Visited);
        Assert.Contains(nameof(CommandStatement), visitor.Visited);
        Assert.Contains(nameof(ContinueStatement), visitor.Visited);
        Assert.Contains(nameof(DefStatement), visitor.Visited);
        Assert.Contains(nameof(DoWhileStatement), visitor.Visited);
        Assert.Contains(nameof(ExecStatement), visitor.Visited);
        Assert.Contains(nameof(ForStatement), visitor.Visited);
        Assert.Contains(nameof(IfStatement), visitor.Visited);
        Assert.Contains(nameof(LoopStatement), visitor.Visited);
        Assert.Contains(nameof(PipeStatement), visitor.Visited);
        Assert.Contains(nameof(ReturnStatement), visitor.Visited);
        Assert.Contains(nameof(WhileStatement), visitor.Visited);
        Assert.True(visitor.TokenCount > 0);
    }

    [Fact]
    public void Walk_AllExpressionTypes_AreVisited()
    {
        var visitor = new RecordingVisitor();

        ParseFilterExpr("1 + 2").Accept(visitor);
        ParseFilterExpr("!$x").Accept(visitor);
        ParseFilterExpr("(1)").Accept(visitor);
        ParseFilterExpr("[1, 2, 3]").Accept(visitor);
        ParseFilterExpr("{ id, status }").Accept(visitor);
        ParseFilterExpr("\"hello $x world\"").Accept(visitor);
        ParseFilterExpr("$items[0]").Accept(visitor);
        ParseFilterExpr(".items[0].id").Accept(visitor);
        ParseFilterExpr("map(.id)").Accept(visitor);
        ParseFilterExpr(".items | length").Accept(visitor);
        ParseFilterExpr("(echo hi)").Accept(visitor);
        ParseFilterExpr("(connect --key=abc)").Accept(visitor);

        Assert.Contains(nameof(BinaryOperatorExpression), visitor.Visited);
        Assert.Contains(nameof(ConstantExpression), visitor.Visited);
        Assert.Contains(nameof(UnaryOperatorExpression), visitor.Visited);
        Assert.Contains(nameof(ParensExpression), visitor.Visited);
        Assert.Contains(nameof(JsonArrayExpression), visitor.Visited);
        Assert.Contains(nameof(JsonExpression), visitor.Visited);
        Assert.Contains(nameof(InterpolatedStringExpression), visitor.Visited);
        Assert.Contains(nameof(JSonPathExpression), visitor.Visited);
        Assert.Contains(nameof(FilterPathExpression), visitor.Visited);
        Assert.Contains(nameof(FilterCallExpression), visitor.Visited);
        Assert.Contains(nameof(FilterPipeExpression), visitor.Visited);
        Assert.Contains(nameof(CommandExpression), visitor.Visited);
        Assert.Contains(nameof(CommandOption), visitor.Visited);
        Assert.True(visitor.TokenCount > 0);
    }

    [Fact]
    public void Walk_DefaultVisitor_DoesNotThrow()
    {
        // A bare AstVisitor subclass with no overrides should traverse without error.
        var visitor = new RecordingVisitor();

        var exception = Record.Exception(() =>
        {
            ParseFilterExpr("1 + 2 * (3 - 4)").Accept(visitor);
            Accept(visitor, "if $x { echo a } else { echo b }");
        });

        Assert.Null(exception);
    }
}
