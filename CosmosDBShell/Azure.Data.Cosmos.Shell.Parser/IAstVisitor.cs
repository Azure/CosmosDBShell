// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

internal interface IAstVisitor
{
    void VisitToken(Token token);

    void Visit(ErrorExpression errorExpression);

    void Visit(ConstantExpression constantExpression);

    void Visit(UnaryOperatorExpression unaryOperatorExpression);

    void Visit(BinaryOperatorExpression binaryOperatorExpression);

    void Visit(ParensExpression parensExpression);

    void Visit(JsonExpression jsonExpression);

    void Visit(JsonArrayExpression jSonArrayExpression);

    void Visit(JSonPathExpression jSonPathExpression);

    void Visit(InterpolatedStringExpression interpolatedStringExpression);

    void Visit(VariableExpression variableExpression);

    void Visit(CommandExpression commandExpression);

    void Visit(CommandOption commandOption);

    // Statement visitors
    void Visit(AssignmentStatement assignmentStatement);

    void Visit(BlockStatement blockStatement);

    void Visit(BreakStatement breakStatement);

    void Visit(CommandStatement commandStatement);

    void Visit(ContinueStatement continueStatement);

    void Visit(DefStatement defStatement);

    void Visit(DoWhileStatement doWhileStatement);

    void Visit(ExecStatement execStatement);

    void Visit(ForStatement forStatement);

    void Visit(IfStatement ifStatement);

    void Visit(LoopStatement loopStatement);

    void Visit(PipeStatement pipeStatement);

    void Visit(ReturnStatement returnStatement);

    void Visit(WhileStatement whileStatement);
}

internal abstract class AstVisitor : IAstVisitor
{
    public virtual void VisitToken(Token token)
    {
    }

    public virtual void Visit(ErrorExpression errorExpression)
    {
    }

    public virtual void Visit(ConstantExpression constantExpression)
    {
        this.VisitToken(constantExpression.Token);
    }

    public virtual void Visit(UnaryOperatorExpression unaryOperatorExpression)
    {
        this.VisitToken(unaryOperatorExpression.OperatorToken);
        unaryOperatorExpression.Expression.Accept(this);
    }

    public virtual void Visit(BinaryOperatorExpression binaryOperatorExpression)
    {
        binaryOperatorExpression.Left.Accept(this);
        this.VisitToken(binaryOperatorExpression.OperatorToken);
        binaryOperatorExpression.Right.Accept(this);
    }

    public virtual void Visit(ParensExpression parensExpression)
    {
        this.VisitToken(parensExpression.LParToken);
        parensExpression.InnerExpression.Accept(this);
        this.VisitToken(parensExpression.RParToken);
    }

    public virtual void Visit(JsonExpression jsonExpression)
    {
    }

    public virtual void Visit(JSonPathExpression jSonPathExpression)
    {
        this.VisitToken(jSonPathExpression.VariableToken);
    }

    public virtual void Visit(JsonArrayExpression jSonArrayExpression)
    {
        this.VisitToken(jSonArrayExpression.LBracketToken);
        foreach (var expression in jSonArrayExpression.Expressions)
        {
            expression.Accept(this);
        }

        this.VisitToken(jSonArrayExpression.RBracketToken);
    }

    public virtual void Visit(InterpolatedStringExpression interpolatedStringExpression)
    {
        this.VisitToken(interpolatedStringExpression.StringToken);
        foreach (var expr in interpolatedStringExpression.Expressions)
        {
            expr.Accept(this);
        }
    }

    public virtual void Visit(VariableExpression variableExpression)
    {
    }

    public virtual void Visit(CommandExpression commandExpression)
    {
        this.VisitToken(commandExpression.CommandToken);
        foreach (var argument in commandExpression.Arguments)
        {
            argument.Accept(this);
        }
    }

    public virtual void Visit(CommandOption commandOption)
    {
    }

    // Statement visitors
    public virtual void Visit(AssignmentStatement assignmentStatement)
    {
        assignmentStatement.Variable.Accept(this);
        this.VisitToken(assignmentStatement.AssignmentToken);
        assignmentStatement.Value.Accept(this);
    }

    public virtual void Visit(BlockStatement blockStatement)
    {
    }

    public virtual void Visit(BreakStatement breakStatement)
    {
    }

    public virtual void Visit(CommandStatement commandStatement)
    {
        this.VisitToken(commandStatement.CommandToken);
        foreach (var argument in commandStatement.Arguments)
        {
            argument.Accept(this);
        }
    }

    public virtual void Visit(ContinueStatement continueStatement)
    {
    }

    public virtual void Visit(DefStatement defStatement)
    {
    }

    public virtual void Visit(DoWhileStatement doWhileStatement)
    {
    }

    public virtual void Visit(ExecStatement execStatement)
    {
    }

    public virtual void Visit(ForStatement forStatement)
    {
    }

    public virtual void Visit(IfStatement ifStatement)
    {
    }

    public virtual void Visit(LoopStatement loopStatement)
    {
    }

    public virtual void Visit(PipeStatement pipeStatement)
    {
        foreach (var statement in pipeStatement.Statements)
        {
            statement.Accept(this);
        }
    }

    public virtual void Visit(ReturnStatement returnStatement)
    {
    }

    public virtual void Visit(WhileStatement whileStatement)
    {
    }
}