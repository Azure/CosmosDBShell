// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents an if-else conditional statement that executes different branches based on a condition.
/// </summary>
/// <remarks>
/// The if statement evaluates a condition and executes one of two possible branches:
/// - If the condition is true, the main statement is executed
/// - If the condition is false and an else clause exists, the else statement is executed
/// - If the condition is false and no else clause exists, execution continues after the if.
/// </remarks>
[AstHelp("statement-if")]
internal class IfStatement : Statement
{
    public IfStatement(Token ifToken, Expression condition, Statement statement, Token? elseToken, Statement? elseStatement)
    {
        this.IfToken = ifToken ?? throw new ArgumentNullException(nameof(ifToken));
        this.Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        this.Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        this.ElseToken = elseToken;
        this.ElseStatement = elseStatement;
    }

    /// <summary>
    /// Gets the 'if' keyword token.
    /// </summary>
    public Token IfToken { get; }

    /// <summary>
    /// Gets the condition expression that determines which branch to execute.
    /// </summary>
    public Expression Condition { get; }

    /// <summary>
    /// Gets the statement to execute when the condition is true.
    /// </summary>
    public Statement Statement { get; }

    /// <summary>
    /// Gets the 'else' keyword token, if present.
    /// </summary>
    public Token? ElseToken { get; }

    /// <summary>
    /// Gets the statement to execute when the condition is false, if present.
    /// </summary>
    public Statement? ElseStatement { get; }

    /// <summary>
    /// Gets the starting position of the if statement in the source text.
    /// </summary>
    public override int Start => this.IfToken.Start;

    /// <summary>
    /// Gets the total length of the if statement in the source text.
    /// </summary>
    public override int Length
    {
        get
        {
            // If there's an else statement, the length extends to the end of it
            if (this.ElseStatement != null)
            {
                return (this.ElseStatement.Start + this.ElseStatement.Length) - this.IfToken.Start;
            }

            // Otherwise, it extends to the end of the main statement
            return (this.Statement.Start + this.Statement.Length) - this.IfToken.Start;
        }
    }

    /// <summary>
    /// Executes the if statement by evaluating the condition and running the appropriate branch.
    /// </summary>
    /// <remarks>
    /// The condition is converted to a boolean value. If true, the main statement executes.
    /// If false and an else clause exists, the else statement executes.
    /// Control flow (break, continue, return) is propagated from the executed branch.
    /// </remarks>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var conditionResult = await this.Condition.EvaluateAsync(shell, commandState, token);
        var boolObj = conditionResult.ConvertShellObject(DataType.Boolean);
        if (boolObj == null)
        {
            throw new InvalidOperationException("Condition evaluation returned null for if statement");
        }

        var result = (bool)boolObj;

        if (result)
        {
            return await this.Statement.RunAsync(shell, commandState, token);
        }
        else if (this.ElseStatement != null)
        {
            return await this.ElseStatement.RunAsync(shell, commandState, token);
        }

        return commandState;
    }

    public override string ToString()
    {
        var elseClause = this.ElseStatement != null ? $" else {this.ElseStatement}" : string.Empty;
        return $"if ({this.Condition}) {this.Statement}{elseClause}";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}