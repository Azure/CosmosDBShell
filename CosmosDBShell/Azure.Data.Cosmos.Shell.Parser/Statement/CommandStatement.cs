// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;
using System.Data;
using System.Reflection;
using System.Text;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos.Linq;
using Spectre.Console;

/// <summary>
/// Represents a command statement that executes a shell command, function, or script.
/// </summary>
/// <remarks>
/// Command statements are the primary way to execute actions in the shell. They support:
/// - Built-in commands (e.g., connect, query, help)
/// - User-defined functions
/// - External script files
/// - Command options (-option or --option)
/// - Output and error redirection (out>, err>, out>>, err>>).
/// </remarks>
internal class CommandStatement : Statement
{
    public CommandStatement(Token commandToken)
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
    public string Name { get => this.CommandToken.Value; }

    /// <summary>
    /// Gets or sets the output redirection token (out> or out>>).
    /// </summary>
    public Token? OutRedirectToken { get; set; }

    /// <summary>
    /// Gets or sets the output redirection destination token.
    /// </summary>
    public Token? OutRedirectDestToken { get; set; }

    /// <summary>
    /// Gets a value indicating whether output should be appended (out>>) rather than overwritten (out>).
    /// </summary>
    public bool AppendOutput { get => this.OutRedirectToken?.Type == TokenType.RedirectAppendOutput; }

    /// <summary>
    /// Gets the output redirection destination filename.
    /// </summary>
    public string? OutputRedirect { get => this.OutRedirectDestToken?.Value; }

    /// <summary>
    /// Gets or sets the error redirection token (err> or err>>).
    /// </summary>
    public Token? ErrRedirectToken { get; set; }

    /// <summary>
    /// Gets or sets the error redirection destination token.
    /// </summary>
    public Token? ErrRedirectDestToken { get; set; }

    /// <summary>
    /// Gets a value indicating whether errors should be appended (err>>) rather than overwritten (err>).
    /// </summary>
    public bool AppendError { get => this.ErrRedirectToken?.Type == TokenType.RedirectAppendError; }

    /// <summary>
    /// Gets the error redirection destination filename.
    /// </summary>
    public string? ErrorRedirect { get => this.ErrRedirectDestToken?.Value; }

    /// <summary>
    /// Gets the list of command arguments and options.
    /// </summary>
    public List<Expression> Arguments { get; } = [];

    /// <summary>
    /// Gets the starting position of the command statement in the source text.
    /// </summary>
    public override int Start => this.CommandToken.Start;

    /// <summary>
    /// Gets the length of the entire command statement in the source text.
    /// </summary>
    public override int Length
    {
        get
        {
            // Find the last token to determine the end position
            var end = this.CommandToken.Start + this.CommandToken.Length;

            // Check error redirection destination (usually comes last)
            if (this.ErrRedirectDestToken != null)
            {
                end = Math.Max(end, this.ErrRedirectDestToken.Start + this.ErrRedirectDestToken.Length);
            }

            // Check output redirection destination
            if (this.OutRedirectDestToken != null)
            {
                end = Math.Max(end, this.OutRedirectDestToken.Start + this.OutRedirectDestToken.Length);
            }

            // Check arguments
            if (this.Arguments.Count > 0)
            {
                var lastArg = this.Arguments[^1];
                var argEnd = lastArg.Start + lastArg.Length;

                // Only use argument end if there's no redirection after it
                if (this.OutRedirectToken == null || this.OutRedirectToken.Start < argEnd)
                {
                    if (this.ErrRedirectToken == null || this.ErrRedirectToken.Start < argEnd)
                    {
                        end = Math.Max(end, argEnd);
                    }
                }
            }

            return end - this.Start;
        }
    }

    /// <summary>
    /// Executes the command statement.
    /// </summary>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
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

        if (shell.App.Commands.TryGetValue(this.Name, out var factory))
        {
            bool showHelp = this.Arguments.OfType<CommandOption>().Any(arg => arg.Name == "help" || arg.Name == "?");
            if (showHelp)
            {
                return HelpCommand.PrintCommandHelp(this.Name, shell.App, false);
            }

            var cmd = await this.CreateCommandAsync(factory, shell, commandState, token);
            return await cmd.ExecuteAsync(shell, commandState, string.Empty, token);
        }

        if (File.Exists(this.Name))
        {
            return await this.RunScriptAsync(shell, commandState, token);
        }

        throw new CommandNotFoundException(this.Name);
    }

    public async Task<CosmosCommand> CreateCommandAsync(CommandFactory factory, ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var cmd = factory.CreateCommand();

        // Build a map of option properties on the command type for quick lookup.
        var optionProperties = cmd.GetType().GetProperties()
            .Select(p => new { Prop = p, Attr = p.GetCustomAttribute<CosmosOptionAttribute>() })
            .Where(x => x.Attr != null)
            .ToList();

        bool IsBoolean(PropertyInfo pi)
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
                continue; // Unknown option; ignore silently (could report later if desired)
            }

            var pi = matchingProperty.Prop;
            var attr = matchingProperty.Attr;

            // If option already has an inline value (e.g. -opt:VAL parsed earlier) leave it.
            if (!IsBoolean(pi) && opt.Value == null)
            {
                int nextIndex = i + 1;
                if (nextIndex < this.Arguments.Count &&
                    this.Arguments[nextIndex] is not CommandOption &&
                    !consumedArgumentIndices.Contains(nextIndex))
                {
                    // Treat next expression as the value of this non-boolean option.
                    opt.Value = this.Arguments[nextIndex];
                    consumedArgumentIndices.Add(nextIndex);
                }
            }

            // Now assign the option value to the command instance.
            if (opt.Value != null)
            {
                var evaluatedValue = await opt.Value.EvaluateAsync(shell, commandState, token);
                var stringValue = evaluatedValue.ConvertShellObject(DataType.Text)?.ToString() ?? string.Empty;

                var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;

                if (targetType == typeof(bool))
                {
                    // Validate boolean string explicitly
                    if (string.Equals(stringValue, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        pi.SetValue(cmd, true);
                    }
                    else if (string.Equals(stringValue, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        pi.SetValue(cmd, false);
                    }
                    else
                    {
                        throw new CommandException(this.Name, $"Invalid boolean value '{stringValue}' for option '{rawName}'. Expected 'true' or 'false'.");
                    }
                }
                else if (targetType.IsEnum)
                {
                    if (Enum.TryParse(targetType, stringValue, ignoreCase: true, out var enumVal))
                    {
                        pi.SetValue(cmd, enumVal);
                    }
                    else
                    {
                        var validValues = string.Join(", ", Enum.GetNames(targetType));
                        throw new CommandException(this.Name, $"Invalid value '{stringValue}' for option '{rawName}'. Valid values are: {validValues}");
                    }
                }
                else if (targetType == typeof(int))
                {
                    if (int.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var intVal))
                    {
                        pi.SetValue(cmd, intVal);
                    }
                    else
                    {
                        throw new CommandException(this.Name, $"Invalid integer value '{stringValue}' for option '{rawName}'.");
                    }
                }
                else if (targetType == typeof(double))
                {
                    if (double.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
                    {
                        pi.SetValue(cmd, dblVal);
                    }
                    else
                    {
                        throw new CommandException(this.Name, $"Invalid numeric value '{stringValue}' for option '{rawName}'.");
                    }
                }
                else
                {
                    // Assign as string; caller can convert later if needed.
                    pi.SetValue(cmd, stringValue);
                }
            }
            else
            {
                // No value provided.
                if (attr?.DefaultValue != null)
                {
                    pi.SetValue(cmd, attr.DefaultValue);
                }
                else if (IsBoolean(pi))
                {
                    // Boolean option present without value => true
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

        // Remaining positional arguments (exclude options and consumed option values)
        var positional = new List<Expression>();
        for (int i = 0; i < this.Arguments.Count; i++)
        {
            if (this.Arguments[i] is CommandOption)
            {
                continue;
            }

            if (consumedArgumentIndices.Contains(i))
            {
                continue; // consumed as option value
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
                    throw new CommandException(this.Name, $"Missing required parameter: {prop.Name}");
                }

                break;
            }

            if (prop.PropertyType.IsArray)
            {
                // Collect all remaining as array
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

        // Too many arguments (when last parameter is not array)
        if (argIndex < positional.Count && parameters.All(p => !p.Prop.PropertyType.IsArray))
        {
            throw new CommandException(this.Name, $"Too many arguments. Expected {parameters.Count}, got {positional.Count}");
        }

        return cmd;
    }

    public async Task<CommandState> RunScriptAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var fileName = this.Name;

        // Each script run gets its own variable frame.
        // This prevents variables created by the script from leaking back into the caller.
        // Additionally, we shadow any pre-existing variables into this frame so that assignments
        // inside the script do not mutate variables in the caller scope.
        var frame = new VariableContainer();

        // Shadow variables from the current scope chain into the script frame.
        // (Copy values so the script can mutate its own bindings.)
        foreach (var container in shell.VariableContainers)
        {
            foreach (var kvp in container.Variables)
            {
                frame.Variables[kvp.Key] = kvp.Value;
            }
        }

        // Positional parameters ($0, $1, ...) override shadows.
        frame.Set("0", new ShellText(fileName));
        for (int i = 0; i < this.Arguments.Count; i++)
        {
            var evaluated = await this.Arguments[i].EvaluateAsync(shell, commandState, token);
            frame.Set((i + 1).ToString(), new ShellText(evaluated.ConvertShellObject(DataType.Text)?.ToString() ?? string.Empty));
        }

        shell.VariableContainers.Enqueue(frame);
        var currentState = commandState;
        string scriptContent = string.Empty;
        var priorFileName = shell.CurrentScriptFileName;
        var priorContent = shell.CurrentScriptContent;

        try
        {
            scriptContent = File.ReadAllText(fileName);
            shell.CurrentScriptFileName = fileName;
            shell.CurrentScriptContent = scriptContent;
            var lexer = new Lexer(scriptContent);
            var parser = new StatementParser(lexer);
            foreach (var statement in parser.ParseStatements())
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    // Run the parsed statements
                    currentState = await statement.RunAsync(shell, currentState, token);
                    if (currentState.IsError)
                    {
                        break;
                    }

                    shell.StdOutRedirect = this.OutputRedirect;
                    shell.AppendOutRedirection = this.AppendOutput;

                    shell.ErrOutRedirect = this.ErrorRedirect;
                    shell.AppendErrRedirection = this.AppendError;

                    try
                    {
                        shell.PrintState(commandState);
                    }
                    finally
                    {
                        shell.StdOutRedirect = null;
                        shell.ErrOutRedirect = null;
                    }
                }
                catch (Exception e)
                {
                    var (line, column, lineText) = PositionalErrorHelper.GetLineAndColumn(scriptContent, statement.Start);
                    throw new PositionalException(fileName, e, line, column, lineText);
                }
            }
        }
        finally
        {
            shell.CurrentScriptFileName = priorFileName;
            shell.CurrentScriptContent = priorContent;

            // Remove the script frame we pushed. Since VariableContainers is a Queue (FIFO),
            // we need to rotate all elements except the last one to the back, then dequeue the last one.
            // Example: [A, B, C] where C (script frame) needs to be removed:
            //   Rotate A: [B, C, A], Rotate B: [C, A, B], Dequeue C: [A, B]
            var count = shell.VariableContainers.Count;
            if (count > 0)
            {
                // Rotate (count - 1) elements to the back
                for (int i = 0; i < count - 1; i++)
                {
                    shell.VariableContainers.Enqueue(shell.VariableContainers.Dequeue());
                }

                // Now the script frame is at the front, dequeue it
                shell.VariableContainers.Dequeue();
            }
        }

        /*
        foreach (var txt in await File.ReadAllLinesAsync(fileName, token))
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            var line = txt.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Equals("@echo off", StringComparison.InvariantCultureIgnoreCase))
            {
                this.Echo = false;
                continue;
            }

            if (line.Equals("@echo on", StringComparison.InvariantCultureIgnoreCase))
            {
                this.Echo = true;
                continue;
            }

            // Replace parameters in the line
            foreach (var kvp in arguments)
            {
                line = line.Replace(kvp.Key, kvp.Text);
            }

            if (this.Echo)
            {
                AnsiConsole.Markup(new CosmosShellPrompt(this).GetPromptString());
                WriteLine(" " + line);
            }

            commandState = await this.RunCommand(commandState, line, token);
            commandState = this.PrintState(commandState);
            if (commandState.IsError)
            {
                break;
            }
        }*/

        return currentState;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(this.Name);

        // Add arguments
        foreach (var arg in this.Arguments)
        {
            if (arg is CommandOption option)
            {
                sb.Append(' ');
                sb.Append(option.Name);

                if (option.Value != null)
                {
                    sb.Append(' ');

                    // For string expressions, we might want to quote them if they contain spaces
                    var valueStr = option.Value.ToString();
                    if (valueStr != null && valueStr.Contains(' ') && !valueStr.StartsWith('"'))
                    {
                        sb.Append('"');
                        sb.Append(valueStr);
                        sb.Append('"');
                    }
                    else
                    {
                        sb.Append(valueStr);
                    }
                }

                continue;
            }

            sb.Append(' ');
            var argStr = arg.ToString();

            // Quote arguments that contain spaces
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
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    /// <summary>
    /// Helper method to convert position to line and column information.
    /// </summary>
    private static (int Line, int Column, string LineText) GetLineAndColumn(string text, int position)
    {
        var line = 1;
        var column = 1;
        var lineStart = 0;

        for (var i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
                lineStart = i + 1;
            }
            else
            {
                column++;
            }
        }

        // Extract the line text
        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var lineText = text[lineStart..lineEnd].TrimEnd('\r');

        return (line, column, lineText);
    }
}