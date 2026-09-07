// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Reflection;
using System.Text;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Represents a command invocation as an expression, allowing commands to be used
/// in expression contexts such as for loops, assignments, and parenthesized expressions.
/// </summary>
/// <remarks>
/// CommandExpression enables syntax like:
/// - for $file in (dir "*.json") { ... }
/// - $result = query "SELECT * FROM c"
/// - echo (get-value --key=foo)
///
/// The command is executed and its result is returned as a ShellObject.
/// </remarks>
internal class CommandExpression : Expression
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandExpression"/> class.
    /// </summary>
    /// <param name="commandToken">The token representing the command name.</param>
    public CommandExpression(Token commandToken)
    {
        this.CommandToken = commandToken ?? throw new ArgumentNullException(nameof(commandToken));
    }

    /// <summary>
    /// Gets the command name token.
    /// </summary>
    public Token CommandToken { get; }

    /// <summary>
    /// Gets the command name.
    /// </summary>
    public string Name => this.CommandToken.Value;

    /// <summary>
    /// Gets the list of command arguments and options.
    /// </summary>
    public List<Expression> Arguments { get; } = [];

    /// <summary>
    /// Gets the starting position of the command expression in the source text.
    /// </summary>
    public override int Start => this.CommandToken.Start;

    /// <summary>
    /// Gets the length of the command expression in the source text.
    /// </summary>
    public override int Length
    {
        get
        {
            if (this.Arguments.Count > 0)
            {
                var lastArg = this.Arguments[^1];
                return (lastArg.Start + lastArg.Length) - this.CommandToken.Start;
            }

            return this.CommandToken.Length;
        }
    }

    /// <summary>
    /// Evaluates the command expression by executing the command and returning its result.
    /// </summary>
    /// <param name="interpreter">The shell interpreter.</param>
    /// <param name="currentState">The current command state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the command execution as a ShellObject.</returns>
    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // Execute the command asynchronously and return the result
        var resultState = await this.ExecuteCommandAsync(interpreter, currentState, cancellationToken);

        if (resultState is ParserErrorCommandState parserError)
        {
            throw new CommandException(this.Name, string.Join("; ", parserError.Errors.Select(error => error.Message)));
        }

        if (resultState.IsError)
        {
            throw new CommandException(this.Name, MessageService.GetArgsString("script-error-command-failed", "name", this.Name));
        }

        // Return the result from the command state
        if (resultState.Result != null)
        {
            return resultState.Result;
        }

        // If no result, return an empty JSON array (safe default for iteration)
        return new ShellJson(JsonSerializer.SerializeToElement(Array.Empty<object>()));
    }

    /// <inheritdoc/>
    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(this.Name);

        foreach (var arg in this.Arguments)
        {
            sb.Append(' ');
            sb.Append(CommandArgumentFormatter.Format(arg));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Executes the command asynchronously.
    /// </summary>
    internal async Task<CommandState> ExecuteCommandAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        // Check for user-defined functions first
        if (shell.Functions.TryGetValue(this.Name, out var function))
        {
            var args = new List<ShellObject>();
            foreach (var a in this.Arguments)
            {
                var evaluated = await a.EvaluateAsync(shell, commandState, token);
                args.Add(evaluated);
            }

            return await function.ExecuteFunctionAsync(shell, commandState, token, args.ToArray());
        }

        // Check for built-in commands
        if (shell.App.Commands.TryGetValue(this.Name, out var factory))
        {
            var cmd = await this.CreateCommandAsync(factory, shell, commandState, token);
            return await shell.ExecuteCosmosCommandAsync(cmd, commandState, string.Empty, token);
        }

        // Check for script files
        if (File.Exists(this.Name))
        {
            return await this.RunScriptAsync(shell, commandState, token);
        }

        throw new CommandNotFoundException(this.Name, SuggestCommand(shell, this.Name), this.Start, this.CommandToken.Length);
    }

    private static string? SuggestCommand(ShellInterpreter shell, string typed)
    {
        IEnumerable<string> candidates = shell.App.Commands.Keys;
        if (shell.Functions.Count > 0)
        {
            candidates = candidates.Concat(shell.Functions.Keys);
        }

        return Azure.Data.Cosmos.Shell.Util.CommandNameSuggester.Suggest(typed, candidates);
    }

    /// <summary>
    /// Creates a command instance with bound parameters and options.
    /// </summary>
    internal Task<CosmosCommand> CreateCommandAsync(CommandFactory factory, ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var statement = new CommandStatement(this.CommandToken);
        statement.Arguments.AddRange(this.Arguments);
        return statement.CreateCommandAsync(factory, shell, commandState, token);
    }

    /// <summary>
    /// Runs a script file.
    /// </summary>
    internal Task<CommandState> RunScriptAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var statement = new CommandStatement(this.CommandToken);
        statement.Arguments.AddRange(this.Arguments);
        return statement.RunScriptAsync(shell, commandState, token, renderOutput: false);
    }
}
