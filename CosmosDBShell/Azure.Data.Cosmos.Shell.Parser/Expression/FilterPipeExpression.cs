// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Linq;
using Azure.Data.Cosmos.Shell.Core;

internal class FilterPipeExpression : Expression
{
    public FilterPipeExpression(Expression left, Token pipeToken, Expression right)
    {
        this.Left = left ?? throw new ArgumentNullException(nameof(left));
        this.PipeToken = pipeToken ?? throw new ArgumentNullException(nameof(pipeToken));
        this.Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public Expression Left { get; }

    public Token PipeToken { get; }

    public Expression Right { get; }

    public override int Start => this.Left.Start;

    public override int Length => this.Right.End - this.Left.Start;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        var leftResult = await this.Left.EvaluateAsync(interpreter, currentState, cancellationToken);

        if (leftResult is ShellSequence sequence)
        {
            var outputs = new List<System.Text.Json.JsonElement>();
            foreach (var element in sequence.Elements)
            {
                var nestedState = new CommandState { Result = new ShellJson(element.Clone()) };
                var rightResult = await this.Right.EvaluateAsync(interpreter, nestedState, cancellationToken);
                if (rightResult is ShellSequence nestedSequence)
                {
                    outputs.AddRange(nestedSequence.Elements.Select(static e => e.Clone()));
                }
                else
                {
                    outputs.Add(FilterExpressionUtilities.ToJsonElement(rightResult));
                }
            }

            return new ShellSequence(outputs);
        }

        var state = new CommandState { Result = leftResult };
        return await this.Right.EvaluateAsync(interpreter, state, cancellationToken);
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}