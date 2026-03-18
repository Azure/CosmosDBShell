// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.ArgumentParser;
using Azure.Data.Cosmos.Shell.Core;

internal class VariableExpression : Expression
{
    private readonly Token variableToken;

    public VariableExpression(Token variableToken, string name)
    {
        this.variableToken = variableToken ?? throw new ArgumentNullException(nameof(variableToken));
        this.Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public Token VariableToken { get => this.variableToken; }

    public override int Start => this.variableToken.Start;

    public override int Length => this.variableToken.Length;

    public override Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        return Task.FromResult(interpreter.GetVariable(this.Name));
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        return $"${this.Name}";
    }
}
