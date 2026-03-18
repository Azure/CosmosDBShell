// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents a function definition statement that declares a reusable function.
/// </summary>
/// <remarks>
/// The def statement creates named functions that can be invoked later with arguments.
/// Functions have their own parameter scope but inherit the parent scope for variables.
/// </remarks>
[AstHelp("statement-def")]
internal class DefStatement : Statement
{
    public DefStatement(Token defToken, Token nameToken, string[] parameters, Statement statement)
    {
        this.DefToken = defToken ?? throw new ArgumentNullException(nameof(defToken));
        this.NameToken = nameToken ?? throw new ArgumentNullException(nameof(nameToken));
        this.Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        this.Statement = statement ?? throw new ArgumentNullException(nameof(statement));
    }

    /// <summary>
    /// Gets the 'def' keyword token.
    /// </summary>
    public Token DefToken { get; }

    /// <summary>
    /// Gets the function name token.
    /// </summary>
    public Token NameToken { get; }

    /// <summary>
    /// Gets the function name.
    /// </summary>
    public string Name { get => this.NameToken.Value; }

    /// <summary>
    /// Gets the array of parameter names for this function.
    /// </summary>
    public string[] Parameters { get; }

    /// <summary>
    /// Gets the statement body of the function.
    /// </summary>
    public Statement Statement { get; }

    /// <summary>
    /// Gets the starting position of the function definition in the source text.
    /// </summary>
    public override int Start => this.DefToken.Start;

    /// <summary>
    /// Gets the total length of the function definition in the source text.
    /// </summary>
    public override int Length => (this.Statement.Start + this.Statement.Length) - this.DefToken.Start;

    /// <summary>
    /// Registers this function definition with the shell interpreter.
    /// </summary>
    /// <remarks>
    /// This method doesn't execute the function body; it only registers the function
    /// so it can be called later by name.
    /// </remarks>
    public override Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        shell.DeclareFunction(this);
        return Task.FromResult(commandState);
    }

    /// <summary>
    /// Executes the function body with the provided arguments.
    /// </summary>
    /// <param name="shell">The shell interpreter context.</param>
    /// <param name="commandState">The current command state.</param>
    /// <param name="token">Cancellation token.</param>
    /// <param name="args">Arguments passed to the function.</param>
    /// <returns>The command state after function execution.</returns>
    /// <remarks>
    /// Creates a new variable scope for parameters, executes the function body,
    /// then restores the previous scope.
    /// </remarks>
    public async Task<CommandState> ExecuteFunctionAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token, params string[] args)
    {
        var arguments = new VariableContainer();

        for (int i = 0; i < this.Parameters.Length && i < args.Length; i++)
        {
            arguments.Set(this.Parameters[i], new ShellText(args[i]));
        }

        shell.VariableContainers.Enqueue(arguments);
        try
        {
            return await this.Statement.RunAsync(shell, commandState, token);
        }
        finally
        {
            // Remove the function frame we pushed. Since VariableContainers is a Queue (FIFO),
            // we need to rotate all elements except the last one to the back, then dequeue the last one.
            var count = shell.VariableContainers.Count;
            if (count > 0)
            {
                // Rotate (count - 1) elements to the back
                for (int i = 0; i < count - 1; i++)
                {
                    shell.VariableContainers.Enqueue(shell.VariableContainers.Dequeue());
                }

                // Now the function frame is at the front, dequeue it
                shell.VariableContainers.Dequeue();
            }
        }
    }

    public override string ToString()
    {
        var parameters = this.Parameters.Length > 0 ? " " + string.Join(" ", this.Parameters) : string.Empty;
        return $"def {this.Name}{parameters} {this.Statement}";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}