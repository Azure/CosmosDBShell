// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents a return statement that exits from a function with an optional value.
/// </summary>
/// <remarks>
/// The return statement immediately exits the current function and optionally provides
/// a return value. When used outside of a function, it has no effect. The return value
/// becomes the result of the function call.
///
/// Syntax:
/// return [expression]
///
/// Examples:
/// return              // Return with no value
/// return 42           // Return a number
/// return "success"    // Return a string
/// return $result      // Return a variable value
///
/// The return statement is typically used in function definitions (def statements) to
/// provide results back to the caller.
/// </remarks>
[AstHelp("statement-return")]
internal class ReturnStatement : Statement
{
    public ReturnStatement(Token returnToken, Expression? value = null)
    {
        this.ReturnToken = returnToken ?? throw new ArgumentNullException(nameof(returnToken));
        this.Value = value;
    }

    /// <summary>
    /// Gets the 'return' keyword token.
    /// </summary>
    public Token ReturnToken { get; }

    /// <summary>
    /// Gets the optional return value expression.
    /// </summary>
    public Expression? Value { get; }

    /// <summary>
    /// Gets the starting position of the return statement in the source text.
    /// </summary>
    public override int Start => this.ReturnToken.Start;

    /// <summary>
    /// Gets the total length of the return statement in the source text.
    /// </summary>
    public override int Length
    {
        get
        {
            // If there's a return value, extend to the end of it
            if (this.Value != null)
            {
                return (this.Value.Start + this.Value.Length) - this.ReturnToken.Start;
            }

            // Otherwise, just the length of the return keyword
            return this.ReturnToken.Length;
        }
    }

    /// <summary>
    /// Executes the return statement by setting the return flag and value in the command state.
    /// </summary>
    /// <remarks>
    /// Sets the ReturnFunc flag to true and evaluates the return expression (if present)
    /// storing it in the command state. The containing function or block is responsible
    /// for checking this flag and propagating the return value.
    /// </remarks>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        commandState.ReturnFunc = true;
        if (this.Value != null)
        {
            commandState.ReturnValue = await this.Value.EvaluateAsync(shell, commandState, token);
        }

        return commandState;
    }

    public override string ToString()
    {
        return this.Value != null ? $"return {this.Value}" : "return";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}