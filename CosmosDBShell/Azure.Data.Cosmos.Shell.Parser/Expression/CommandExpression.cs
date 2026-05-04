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
            var argStr = arg.ToString();

            if (argStr != null && argStr.Contains(' ') && !argStr.StartsWith('"'))
            {
                sb.Append('"');
                sb.Append(argStr);
                sb.Append('"');
            }
            else
            {
                sb.Append(argStr);
            }
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
            var args = new List<string>();
            foreach (var a in this.Arguments)
            {
                var evaluated = await a.EvaluateAsync(shell, commandState, token);
                args.Add(evaluated?.ConvertShellObject(DataType.Text)?.ToString() ?? string.Empty);
            }

            return await function.ExecuteFunctionAsync(shell, commandState, token, args.ToArray());
        }

        // Check for built-in commands
        if (shell.App.Commands.TryGetValue(this.Name, out var factory))
        {
            var cmd = await this.CreateCommandAsync(factory, shell, commandState, token);
            return await cmd.ExecuteAsync(shell, commandState, string.Empty, token);
        }

        // Check for script files
        if (File.Exists(this.Name))
        {
            return await this.RunScriptAsync(shell, commandState, token);
        }

        throw new CommandNotFoundException(this.Name);
    }

    /// <summary>
    /// Creates a command instance with bound parameters and options.
    /// </summary>
    internal async Task<CosmosCommand> CreateCommandAsync(CommandFactory factory, ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var cmd = factory.CreateCommand();

        // Build a map of option properties on the command type for quick lookup.
        var optionProperties = cmd.GetType().GetProperties()
            .Select(p => new { Prop = p, Attr = p.GetCustomAttribute<CosmosOptionAttribute>() })
            .Where(x => x.Attr != null)
            .ToList();

        bool IsBoolean(System.Reflection.PropertyInfo pi)
            => (Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType) == typeof(bool);

        // First pass: bind option values (including space-separated values) and record which argument indices are consumed.
        var consumedArgumentIndices = new HashSet<int>();

        for (int i = 0; i < this.Arguments.Count; i++)
        {
            if (this.Arguments[i] is not CommandOption opt)
            {
                continue;
            }

            var rawName = opt.Name.TrimStart('-');
            var matchingProperty = optionProperties
                .FirstOrDefault(x => x.Attr!.Names.Contains(rawName, StringComparer.OrdinalIgnoreCase));

            if (matchingProperty == null)
            {
                throw new CommandException(this.Name, $"Unknown option '{rawName}'.");
            }

            var pi = matchingProperty.Prop;
            var attr = matchingProperty.Attr;

            // For boolean options, the presence of the option is enough. Do not consume the next
            // positional argument as a value (e.g. `dir "*.csh" -l` should not treat -l as taking
            // the filter as its value).
            if (IsBoolean(pi))
            {
                if (opt.Value == null)
                {
                    opt.Value = new ConstantExpression(new Token(TokenType.Identifier, "true", 0, 0), new ShellText("true"));
                }
            }
            else if (opt.Value == null)
            {
                int nextIndex = i + 1;
                if (nextIndex < this.Arguments.Count &&
                    this.Arguments[nextIndex] is not CommandOption &&
                    !consumedArgumentIndices.Contains(nextIndex))
                {
                    opt.Value = this.Arguments[nextIndex];
                    consumedArgumentIndices.Add(nextIndex);
                }
            }

            if (opt.Value != null)
            {
                var evaluatedValue = await opt.Value.EvaluateAsync(shell, commandState, token);
                var stringValue = evaluatedValue.ConvertShellObject(DataType.Text)?.ToString() ?? string.Empty;

                var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
                pi.SetValue(cmd, CommandOptionBinder.ConvertOptionValue(this.Name, rawName, stringValue, targetType));
            }
            else
            {
                if (attr?.DefaultValue != null)
                {
                    pi.SetValue(cmd, attr.DefaultValue);
                }
                else if (IsBoolean(pi))
                {
                    pi.SetValue(cmd, true);
                }
                else
                {
                    throw new CommandException(this.Name, $"Option '{rawName}' requires a value.");
                }
            }
        }

        // Collect parameter properties
        var parameters = cmd.GetType().GetProperties()
            .Select(p => new { Prop = p, Attr = p.GetCustomAttribute<CosmosParameterAttribute>() })
            .Where(x => x.Attr != null)
            .ToList();

        // Remaining positional arguments
        var positional = new List<Expression>();
        for (int i = 0; i < this.Arguments.Count; i++)
        {
            if (this.Arguments[i] is CommandOption)
            {
                continue;
            }

            if (consumedArgumentIndices.Contains(i))
            {
                continue;
            }

            positional.Add(this.Arguments[i]);
        }

        // Bind positional parameters
        int argIndex = 0;
        foreach (var param in parameters)
        {
            var prop = param.Prop;
            var attr = param.Attr!;
            if (argIndex >= positional.Count)
            {
                if (attr.IsRequired)
                {
                    var message = !string.IsNullOrEmpty(attr.RequiredErrorKey)
                        ? MessageService.GetString(attr.RequiredErrorKey)
                        : $"Missing required parameter: {prop.Name}";
                    throw new CommandException(this.Name, message);
                }

                break;
            }

            if (prop.PropertyType.IsArray)
            {
                var arr = new List<string>();
                while (argIndex < positional.Count)
                {
                    var evaluatedArg = await positional[argIndex].EvaluateAsync(shell, commandState, token);
                    var stringValue = evaluatedArg.ConvertShellObject(DataType.Text)?.ToString() ?? string.Empty;
                    arr.Add(stringValue);
                    argIndex++;
                }

                prop.SetValue(cmd, arr.ToArray());
            }
            else
            {
                var evaluatedArg = await positional[argIndex].EvaluateAsync(shell, commandState, token);
                var stringValue = evaluatedArg.ConvertShellObject(DataType.Text)?.ToString() ?? string.Empty;
                Parameter.SetValue(cmd, prop, stringValue);
                argIndex++;
            }
        }

        if (argIndex < positional.Count && parameters.All(p => !p.Prop.PropertyType.IsArray))
        {
            throw new CommandException(this.Name, $"Too many arguments. Expected {parameters.Count}, got {positional.Count}");
        }

        return cmd;
    }

    /// <summary>
    /// Runs a script file.
    /// </summary>
    internal async Task<CommandState> RunScriptAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var fileName = this.Name;

        var arguments = new VariableContainer();
        arguments.Set("0", new ShellText(fileName));

        for (int i = 0; i < this.Arguments.Count; i++)
        {
            var evaluated = await this.Arguments[i].EvaluateAsync(shell, commandState, token);
            arguments.Set((i + 1).ToString(), new ShellText(evaluated.ConvertShellObject(DataType.Text)?.ToString() ?? string.Empty));
        }

        shell.VariableContainers.Enqueue(arguments);
        var currentState = commandState;

        try
        {
            var scriptContent = File.ReadAllText(fileName);
            var lexer = new Lexer(scriptContent);
            var parser = new StatementParser(lexer);
            foreach (var statement in parser.ParseStatements())
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                currentState = await statement.RunAsync(shell, currentState, token);
                if (currentState.IsError)
                {
                    break;
                }
            }
        }
        finally
        {
            shell.VariableContainers.Dequeue();
        }

        return currentState;
    }
}
