// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents an infinite loop statement that executes until explicitly broken.
/// </summary>
/// <remarks>
/// The loop statement creates an infinite loop that continues executing its body
/// until a break statement is encountered or an error occurs. This is useful for
/// creating event loops, waiting for conditions, or implementing retry logic.
///
/// Syntax:
/// loop
///     statement
///
/// Example:
/// $count = 0
/// loop
///     $count = $count + 1
///     echo $count
///     if $count >= 5
///         break
///
/// The loop must contain a break condition to avoid infinite execution.
/// </remarks>
[AstHelp("statement-loop")]
internal class LoopStatement : Statement
{
    public LoopStatement(Token loopToken, Statement statement)
    {
        this.LoopToken = loopToken ?? throw new ArgumentNullException(nameof(loopToken));
        this.Statement = statement ?? throw new ArgumentNullException(nameof(statement));
    }

    /// <summary>
    /// Gets the 'loop' keyword token.
    /// </summary>
    public Token LoopToken { get; }

    /// <summary>
    /// Gets the statement body executed in each iteration.
    /// </summary>
    public Statement Statement { get; }

    /// <summary>
    /// Gets the starting position of the loop statement in the source text.
    /// </summary>
    public override int Start => this.LoopToken.Start;

    /// <summary>
    /// Gets the total length of the loop statement in the source text.
    /// </summary>
    public override int Length => (this.Statement.Start + this.Statement.Length) - this.LoopToken.Start;

    /// <summary>
    /// Executes the infinite loop until a break statement is encountered.
    /// </summary>
    /// <remarks>
    /// The loop continues indefinitely until:
    /// - A break statement is executed within the loop body
    /// - An error occurs during execution
    /// - The cancellation token is triggered
    ///
    /// Note: continue statements within the loop will skip to the next iteration
    /// without checking any condition.
    /// </remarks>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        while (true)
        {
            try
            {
                commandState = await this.Statement.RunAsync(shell, commandState, token);
            }
            catch (PositionalException)
            {
                throw;
            }
            catch (Exception e)
            {
                var content = shell.CurrentScriptContent;
                var fileName = shell.CurrentScriptFileName;
                if (!string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(fileName))
                {
                    var (line, column, lineText) = PositionalErrorHelper.GetLineAndColumn(content, this.Statement.Start);
                    throw new PositionalException(fileName, e, line, column, lineText);
                }

                throw;
            }

            if (commandState.BreakBlock)
            {
                commandState.BreakBlock = false; // Reset break state
                return commandState;
            }
        }
    }

    public override string ToString()
    {
        return $"loop {this.Statement}";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}