// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents a continue statement that skips to the next iteration of the current loop.
/// </summary>
/// <remarks>
/// The continue statement causes the program to skip the rest of the current
/// loop iteration and move to the next iteration. It can only be used inside
/// loop constructs (for, while, do-while, or loop statements).
///
/// Example:
/// for $i in [1, 2, 3, 4, 5]
///     if $i == 3
///         continue
///     echo $i
///
/// This will output: 1, 2, 4, 5 (skipping 3).
/// </remarks>
[AstHelp("statement-continue")]
internal class ContinueStatement : Statement
{
    public ContinueStatement(Token continueToken)
    {
        this.ContinueToken = continueToken ?? throw new ArgumentNullException(nameof(continueToken));
    }

    /// <summary>
    /// Gets the 'continue' keyword token.
    /// </summary>
    public Token ContinueToken { get; }

    /// <summary>
    /// Gets the starting position of the continue statement in the source text.
    /// </summary>
    public override int Start => this.ContinueToken.Start;

    /// <summary>
    /// Gets the length of the continue statement in the source text.
    /// </summary>
    public override int Length => this.ContinueToken.Length;

    /// <summary>
    /// Executes the continue statement by setting the continue flag in the command state.
    /// </summary>
    /// <remarks>
    /// When executed, this sets the ContinueBlock flag to true, which signals
    /// the containing loop to skip to the next iteration.
    /// </remarks>
    public override Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        commandState.ContinueBlock = true;
        return Task.FromResult(commandState);
    }

    public override string ToString()
    {
        return "continue";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}