// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text;

using Azure.Data.Cosmos.Shell.Core;

internal class InterpolatedStringExpression : Expression
{
    private readonly Token stringToken;

    public InterpolatedStringExpression(Token stringToken, List<Expression> expressions)
    {
        this.Expressions = expressions ?? throw new ArgumentNullException(nameof(expressions));
        this.stringToken = stringToken ?? throw new ArgumentNullException(nameof(stringToken));
    }

    public List<Expression> Expressions { get; }

    public Token StringToken { get => this.stringToken; }

    public override int Start => this.stringToken.Start;

    public override int Length => this.stringToken.Length;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        foreach (var expr in this.Expressions)
        {
            var evaluatedResult = await expr.EvaluateAsync(interpreter, currentState, cancellationToken);
            var evaluated = evaluatedResult.ConvertShellObject(DataType.Text) as string;
            if (evaluated != null)
            {
                result.Append(evaluated);
            }
        }

        return new ShellText(result.ToString());
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        return $"\"{{interpolated string with {this.Expressions.Count} expression(s)}}\"";
    }
}
