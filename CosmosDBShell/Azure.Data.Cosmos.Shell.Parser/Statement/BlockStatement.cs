// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents a block statement containing a sequence of statements enclosed in braces.
/// </summary>
/// <remarks>
/// Block statements create a new scope for execution and are used in control structures
/// like if/else, loops, and function bodies. They handle control flow propagation
/// including break, continue, and return statements.
/// </remarks>
internal class BlockStatement : Statement
{
    private readonly Token? openBraceToken;
    private readonly Token? closeBraceToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockStatement"/> class with brace tokens.
    /// </summary>
    /// <param name="openBrace">The opening brace token.</param>
    /// <param name="closeBrace">The closing brace token.</param>
    public BlockStatement(Token openBrace, Token closeBrace, List<Statement> statements)
    {
        this.openBraceToken = openBrace;
        this.closeBraceToken = closeBrace;
        this.Statements = statements ?? throw new ArgumentNullException(nameof(statements));
    }

    /// <summary>
    /// Gets the list of statements contained within this block.
    /// </summary>
    public List<Statement> Statements { get; } = new List<Statement>();

    /// <summary>
    /// Gets the starting position of the block statement in the source text.
    /// </summary>
    public override int Start => this.openBraceToken?.Start ?? (this.Statements.Count > 0 ? this.Statements[0].Start : 0);

    /// <summary>
    /// Gets the total length of the block statement in the source text.
    /// </summary>
    public override int Length
    {
        get
        {
            if (this.closeBraceToken != null && this.openBraceToken != null)
            {
                return (this.closeBraceToken.Start + this.closeBraceToken.Length) - this.openBraceToken.Start;
            }

            if (this.Statements.Count > 0)
            {
                var lastStatement = this.Statements[^1];
                return (lastStatement.Start + lastStatement.Length) - this.Start;
            }

            return 0;
        }
    }

    /// <summary>
    /// Executes all statements in the block sequentially, handling control flow.
    /// </summary>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        foreach (var statement in this.Statements)
        {
            try
            {
                commandState = await statement.RunAsync(shell, commandState, token);
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
                    var (line, column, lineText) = PositionalErrorHelper.GetLineAndColumn(content, statement.Start);
                    throw new PositionalException(fileName, e, line, column, lineText);
                }

                throw;
            }

            shell.PrintState(commandState);
            if (commandState.IsError)
            {
                return commandState;
            }

            // Propagate break out of this block (do not clear it here)
            if (commandState.BreakBlock)
            {
                return commandState;
            }

            if (commandState.ContinueBlock)
            {
                commandState.ContinueBlock = false; // Reset continue state
                return commandState;
            }

            if (commandState.ReturnFunc)
            {
                commandState.ReturnFunc = false; // Reset return state

                if (commandState.ReturnValue != null)
                {
                    commandState.Result = commandState.ReturnValue;
                    commandState.ReturnValue = null; // Reset return value
                }

                return commandState;
            }
        }

        return commandState;
    }

    public override string ToString()
    {
        var statements = string.Join("; ", this.Statements.Select(s => s.ToString()));
        return $"{{ {statements} }}";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}