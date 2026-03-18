// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents a while loop statement that executes a statement repeatedly as long as a condition is true.
/// </summary>
/// <remarks>
/// The while loop evaluates its condition before each iteration. If the condition is false
/// initially, the loop body never executes. The loop continues until the condition becomes
/// false or a break statement is encountered.
/// </remarks>
[AstHelp("statement-while")]
internal class WhileStatement : Statement
{
    public WhileStatement(Token whileToken, Expression condition, Statement statement)
    {
        this.WhileToken = whileToken ?? throw new ArgumentNullException(nameof(whileToken));
        this.Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        this.Statement = statement ?? throw new ArgumentNullException(nameof(statement));
    }

    /// <summary>
    /// Gets the 'while' keyword token.
    /// </summary>
    public Token WhileToken { get; }

    /// <summary>
    /// Gets the condition expression evaluated before each iteration.
    /// </summary>
    public Expression Condition { get; }

    /// <summary>
    /// Gets the statement body executed in each iteration.
    /// </summary>
    public Statement Statement { get; }

    /// <summary>
    /// Gets the starting position of the while statement in the source text.
    /// </summary>
    public override int Start => this.WhileToken.Start;

    /// <summary>
    /// Gets the total length of the while statement in the source text.
    /// </summary>
    public override int Length => (this.Statement.Start + this.Statement.Length) - this.WhileToken.Start;

    /// <summary>
    /// Executes the while loop, evaluating the condition before each iteration.
    /// </summary>
    /// <remarks>
    /// The loop continues while:
    /// - The condition evaluates to true
    /// - No error has occurred
    /// - No break statement has been encountered
    ///
    /// The condition is evaluated before each iteration, so if it's initially false,
    /// the loop body never executes.
    /// </remarks>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        while (!commandState.IsError && await this.EvaluateConditionAsync(shell, commandState, token))
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

        return commandState;
    }

    public override string ToString()
    {
        return $"while ({this.Condition}) {this.Statement}";
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
            throw new InvalidOperationException("Condition evaluation returned null for while statement");
        }

        return (bool)boolObj;
    }
}