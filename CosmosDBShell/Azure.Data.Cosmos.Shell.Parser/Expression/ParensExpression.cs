// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

internal class ParensExpression : Expression
{
    private readonly Token lparToken;
    private readonly Token rparToken;

    public ParensExpression(Token lparToken, Expression innerExpression, Token rparToken)
    {
        this.InnerExpression = innerExpression ?? throw new ArgumentNullException(nameof(innerExpression));
        this.lparToken = lparToken ?? throw new ArgumentNullException(nameof(lparToken));
        this.rparToken = rparToken ?? throw new ArgumentNullException(nameof(rparToken));
    }

    public Expression InnerExpression { get; }

    public Token LParToken { get => this.lparToken; }

    public Token RParToken { get => this.rparToken; }

    public override int Start => this.lparToken.Start;

    public override int Length => this.rparToken.Start - this.lparToken.Start + 1;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // Simply evaluate the inner expression
        return await this.InnerExpression.EvaluateAsync(interpreter, currentState, cancellationToken);
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        return $"({this.InnerExpression})";
    }
}
