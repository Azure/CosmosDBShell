// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;

using Azure.Data.Cosmos.Shell.Core;

internal class AssignmentStatement : Statement
{
    public AssignmentStatement(VariableExpression variable, Token assignmentToken, Expression value)
    {
        this.Variable = variable ?? throw new ArgumentNullException(nameof(variable));
        this.AssignmentToken = assignmentToken ?? throw new ArgumentNullException(nameof(assignmentToken));
        this.Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the starting position of the assignment statement in the source text.
    /// </summary>
    public override int Start => this.Variable.Start;

    /// <summary>
    /// Gets the total length of the assignment statement in the source text.
    /// </summary>
    public override int Length => (this.Value.Start + this.Value.Length) - this.Variable.Start;

    public VariableExpression Variable { get; }

    public Token AssignmentToken { get; }

    public Expression Value { get; }

    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        Expression expression = this.Value;
        if (this.AssignmentToken.Value != "=")
        {
            var operatorType = this.AssignmentToken.Value switch
            {
                "+=" => TokenType.Plus,
                "-=" => TokenType.Minus,
                "*=" => TokenType.Multiply,
                "/=" => TokenType.Divide,
                _ => throw new InvalidOperationException($"Unsupported assignment operator '{this.AssignmentToken.Value}'."),
            };
            var operatorToken = new Token(operatorType, this.AssignmentToken.Value[..1], this.AssignmentToken.Start, 1);
            expression = new BinaryOperatorExpression(this.Variable, operatorToken, this.Value);
        }

        var value = await expression.EvaluateAsync(shell, commandState, token);
        shell.SetVariable(this.Variable.Name, value);
        commandState.Result = null;
        commandState.RenderUser = null;
        commandState.RenderTabular = null;
        commandState.ResetOutputFormat();
        commandState.OutputRendered = false;
        return commandState;
    }

    public override string ToString()
    {
        return $"{this.Variable} {this.AssignmentToken.Value} {this.Value}";
    }

    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}
