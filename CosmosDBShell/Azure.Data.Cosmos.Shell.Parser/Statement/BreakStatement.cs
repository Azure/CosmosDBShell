// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents a break statement that exits the current loop.
/// </summary>
/// <remarks>
/// The break statement immediately terminates execution of the innermost
/// enclosing loop (for, while, do-while, or loop statement) and continues
/// execution after the loop.
/// </remarks>
[AstHelp("statement-break")]
internal class BreakStatement : Statement
{
    public BreakStatement(Token breakToken)
    {
        this.BreakToken = breakToken ?? throw new ArgumentNullException(nameof(breakToken));
    }

    /// <summary>
    /// Gets the 'break' keyword token.
    /// </summary>
    public Token BreakToken { get; }

    /// <summary>
    /// Gets the starting position of the break statement in the source text.
    /// </summary>
    public override int Start => this.BreakToken.Start;

    /// <summary>
    /// Gets the length of the break statement in the source text.
    /// </summary>
    public override int Length => this.BreakToken.Length;

    /// <summary>
    /// Executes the break statement by setting the break flag in the command state.
    /// </summary>
    public override Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        commandState.BreakBlock = true;
        return Task.FromResult(commandState);
    }

    public override string ToString()
    {
        return "break";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}