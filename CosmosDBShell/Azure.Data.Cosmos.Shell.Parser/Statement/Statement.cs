// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Represents an abstract base class for all statement types in the shell parser.
/// </summary>
/// <remarks>
/// Statements are executable units that can perform actions such as:
/// - Executing commands
/// - Control flow (if, while, for, loop)
/// - Variable assignment
/// - Function definitions
/// - Break, continue, and return operations
/// Each statement knows its position in the source text and can execute asynchronously.
/// </remarks>
public abstract class Statement
{
    /// <summary>
    /// Gets the starting position of the statement in the source text.
    /// </summary>
    public abstract int Start { get; }

    /// <summary>
    /// Gets the length of the statement in the source text.
    /// </summary>
    public abstract int Length { get; }

    /// <summary>
    /// Executes the statement asynchronously.
    /// </summary>
    /// <param name="shell">The shell interpreter context.</param>
    /// <param name="commandState">The current command state.</param>
    /// <param name="token">Cancellation token for the operation.</param>
    /// <returns>A task representing the command state after execution.</returns>
    public abstract Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token);

    /// <summary>
    /// Returns a string representation of the statement including its position and length.
    /// </summary>
    /// <returns>A string describing the statement type and position.</returns>
    public override string ToString()
    {
        return $"{this.GetType().Name} [Start={this.Start}, Length={this.Length}]";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    /// <param name="visitor">The visitor to accept.</param>
    internal abstract void Accept(IAstVisitor visitor);
}
