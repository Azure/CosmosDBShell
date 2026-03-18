// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents an exec statement that dynamically evaluates an expression to get a command
/// or script path and executes it with optional arguments.
/// </summary>
/// <remarks>
/// The exec statement is used to execute dynamically determined commands or scripts.
/// The first expression is evaluated to get the command/script path, and subsequent
/// expressions are passed as arguments.
///
/// Syntax:
/// exec expression [arg1] [arg2] ...
///
/// Example:
/// exec $script.path arg1 arg2
/// exec "myscript.csh" --option value
///
/// This is useful when the command to execute is stored in a variable or computed
/// at runtime.
/// </remarks>
[AstHelp("statement-exec")]
internal class ExecStatement : Statement
{
    public ExecStatement(Token execToken, Expression commandExpression, List<Expression> arguments)
    {
        this.ExecToken = execToken ?? throw new ArgumentNullException(nameof(execToken));
        this.CommandExpression = commandExpression ?? throw new ArgumentNullException(nameof(commandExpression));
        this.Arguments = arguments ?? [];
    }

    /// <summary>
    /// Gets the 'exec' keyword token.
    /// </summary>
    public Token ExecToken { get; }

    /// <summary>
    /// Gets the expression that evaluates to the command or script path.
    /// </summary>
    public Expression CommandExpression { get; }

    /// <summary>
    /// Gets the list of argument expressions to pass to the command.
    /// </summary>
    public List<Expression> Arguments { get; }

    /// <summary>
    /// Gets the starting position of the exec statement in the source text.
    /// </summary>
    public override int Start => this.ExecToken.Start;

    /// <summary>
    /// Gets the total length of the exec statement in the source text.
    /// </summary>
    public override int Length
    {
        get
        {
            if (this.Arguments.Count > 0)
            {
                var lastArg = this.Arguments[^1];
                return (lastArg.Start + lastArg.Length) - this.ExecToken.Start;
            }

            return (this.CommandExpression.Start + this.CommandExpression.Length) - this.ExecToken.Start;
        }
    }

    /// <summary>
    /// Executes the exec statement by evaluating the command expression and running
    /// the resulting command or script with the provided arguments.
    /// </summary>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        // Evaluate the command expression to get the command/script path
        var commandValue = await this.CommandExpression.EvaluateAsync(shell, commandState, token);
        var commandPath = commandValue.ConvertShellObject(DataType.Text)?.ToString();

        if (string.IsNullOrEmpty(commandPath))
        {
            throw new InvalidOperationException("exec: command expression evaluated to empty or null. Expected a valid command or script path.");
        }

        // Allow passing a quoted path: exec "C:\path\file.csh"
        commandPath = commandPath.Trim();
        if (commandPath.Length >= 2 && commandPath[0] == '"' && commandPath[^1] == '"')
        {
            commandPath = commandPath[1..^1];
        }

        // Build a CommandStatement dynamically
        var commandToken = new Token(TokenType.Identifier, commandPath, this.CommandExpression.Start, commandPath.Length);
        var dynamicCommand = new CommandStatement(commandToken);

        // Add evaluated arguments
        foreach (var arg in this.Arguments)
        {
            dynamicCommand.Arguments.Add(arg);
        }

        // Execute the command
        try
        {
            // If the evaluated command is a file path, execute it as a shell script directly.
            // This avoids treating the path as an external command (which on Windows yields
            // 'is not recognized as an internal or external command...').
            if (File.Exists(commandPath))
            {
                return await dynamicCommand.RunScriptAsync(shell, commandState, token);
            }

            return await dynamicCommand.RunAsync(shell, commandState, token);
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
                var (line, column, lineText) = PositionalErrorHelper.GetLineAndColumn(content, this.Start);
                throw new PositionalException(fileName, e, line, column, lineText);
            }

            throw;
        }
    }

    public override string ToString()
    {
        var args = string.Join(" ", this.Arguments.Select(a => a.ToString()));
        return string.IsNullOrEmpty(args)
            ? $"exec {this.CommandExpression}"
            : $"exec {this.CommandExpression} {args}";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}
