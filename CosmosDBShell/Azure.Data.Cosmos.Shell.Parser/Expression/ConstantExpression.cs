// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

internal class ConstantExpression : Expression
{
    public ConstantExpression(Token token, ShellObject value)
    {
        this.Value = value ?? throw new ArgumentNullException(nameof(value));
        this.Token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public Token Token { get; }

    public ShellObject Value { get; }

    public override int Start { get => this.Token.Start; }

    public override int Length { get => this.Token.Length; }

    public override Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        return Task.FromResult(this.Value);
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        return this.Token.Value;
    }
}
