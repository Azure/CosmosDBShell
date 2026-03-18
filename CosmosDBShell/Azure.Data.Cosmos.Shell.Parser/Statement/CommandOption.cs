// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;
using Azure.Data.Cosmos.Shell.Core;

internal class CommandOption : Expression
{
    public CommandOption(Token minusToken, Token nameToken, Expression? value = null)
    {
        this.MinusToken = minusToken ?? throw new ArgumentNullException(nameof(minusToken));
        this.NameToken = nameToken ?? throw new ArgumentNullException(nameof(nameToken));
        this.Value = value;
    }

    public Token MinusToken { get; }

    public Token NameToken { get; }

    public string Name { get => this.NameToken.Value; }

    public Expression? Value { get; set; }

    public override int Start => this.MinusToken.Start;

    public override int Length => (this.Value != null ? this.Value.Start + this.Value.Length : this.NameToken.Start + this.NameToken.Length) - this.MinusToken.Start;

    public override Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // should never be called directly, only used in CommandStatement
        throw new NotImplementedException();
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}
