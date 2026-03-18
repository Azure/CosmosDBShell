// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents a do-while loop statement that executes a statement at least once before checking a condition.
/// </summary>
/// <remarks>
/// The do-while loop executes its body first, then evaluates the condition. If the condition
/// is true, the loop continues. This guarantees that the loop body executes at least once,
/// unlike a regular while loop.
/// </remarks>
[AstHelp("statement-do")]
internal class DoWhileStatement : Statement
{
    public DoWhileStatement(Token doToken, Statement statement, Token whileToken, Expression condition)
    {
        this.DoToken = doToken ?? throw new ArgumentNullException(nameof(doToken));
        this.Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        this.WhileToken = whileToken ?? throw new ArgumentNullException(nameof(whileToken));
        this.Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }

    /// <summary>
    /// Gets the 'do' keyword token.
    /// </summary>
    public Token DoToken { get; }

    /// <summary>
    /// Gets the statement body executed in each iteration.
    /// </summary>
    public Statement Statement { get; }

    /// <summary>
    /// Gets the 'while' keyword token.
    /// </summary>
    public Token WhileToken { get; }

    /// <summary>
    /// Gets the condition expression evaluated after each iteration.
    /// </summary>
    public Expression Condition { get; }

    /// <summary>
    /// Gets the starting position of the do-while statement in the source text.
    /// </summary>
    public override int Start => this.DoToken.Start;

    /// <summary>
    /// Gets the total length of the do-while statement in the source text.
    /// </summary>
    public override int Length => (this.Condition.Start + this.Condition.Length) - this.DoToken.Start;

    /// <summary>
    /// Executes the do-while loop, running the body at least once before checking the condition.
    /// </summary>
    /// <remarks>
    /// The loop continues until:
    /// - The condition evaluates to false
    /// - A break statement is encountered
    /// - An error occurs
    /// - The cancellation token is triggered.
    /// </remarks>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        do
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
        while (!commandState.IsError && await this.EvaluateConditionAsync(shell, commandState, token));

        return commandState;
    }

    public override string ToString()
    {
        return $"do {this.Statement} while ({this.Condition})";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    /// <summary>
    /// Evaluates the loop condition to a boolean value.
    /// </summary>
    private async Task<bool> EvaluateConditionAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var conditionResult = await this.Condition.EvaluateAsync(shell, commandState, token);
        var boolObj = conditionResult.ConvertShellObject(DataType.Boolean);
        if (boolObj == null)
        {
            throw new InvalidOperationException("Condition evaluation returned null for do-while statement");
        }

        return (bool)boolObj;
    }
}