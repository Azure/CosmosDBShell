// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

internal abstract class Expression
{
    public abstract int Start { get; }

    public abstract int Length { get; }

    public int End { get => this.Start + this.Length; }

    public static Expression Parse(Lexer lexer)
    {
        var parser = new ExpressionParser(lexer);
        return parser.ParseExpression();
    }

    /// <summary>
    /// Evaluates the expression asynchronously.
    /// </summary>
    /// <param name="interpreter">The shell interpreter context.</param>
    /// <param name="currentState">The current command state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The evaluated shell object.</returns>
    public abstract Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken);

    /// <summary>
    /// Evaluates the expression synchronously. This is a convenience wrapper around EvaluateAsync.
    /// </summary>
    [Obsolete("Use EvaluateAsync instead for better async support.")]
#pragma warning disable VSTHRD002 // Synchronously waiting on tasks - intentional for backward compatibility
    public ShellObject Evaluate(ShellInterpreter interpreter, CommandState currentState)
    {
        return this.EvaluateAsync(interpreter, currentState, CancellationToken.None).GetAwaiter().GetResult();
    }
#pragma warning restore VSTHRD002

    public abstract void Accept(IAstVisitor visitor);

    public override string ToString()
    {
        return $"{this.GetType().Name} [Start={this.Start}, Length={this.Length}]";
    }
}
