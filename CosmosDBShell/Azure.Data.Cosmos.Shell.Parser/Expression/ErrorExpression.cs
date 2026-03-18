// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

internal class ErrorExpression(int start, int length) : Expression
{
    public override int Start { get; } = start;

    public override int Length { get; } = length;

    public override Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // Return an empty string for error expressions instead of null
        return Task.FromResult<ShellObject>(new ShellText(string.Empty));
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        return "<error>";
    }
}
