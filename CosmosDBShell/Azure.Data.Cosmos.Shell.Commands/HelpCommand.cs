//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Linq;
using System.Reflection;
using System.Text;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using CommandLine.Text;
using RadLine;
using Spectre.Console;
using static System.Net.Mime.MediaTypeNames;
using static Azure.Data.Cosmos.Shell.Core.ShellInterpreter;

[CosmosCommand("help")]
[CosmosExample("help", Description = "Display list of all available commands")]
[CosmosExample("help query", Description = "Show detailed help for the query command")]
[CosmosExample("help --details", Description = "Show detailed help for all commands")]
internal class HelpCommand : CosmosCommand
{
    private const int ARGPADDING = 20;
    private const string INDENT = "  ";

    [CosmosOption("details", "d")]
    public bool Details { get; init; }

    // Disable styling / panels / colors for script or limited terminals
    [CosmosOption("plain", "no-style")]
    public bool Plain { get; init; }

    [CosmosParameter("command", IsRequired = false)]
    public string? Command { get; init; }

    public static CommandState PrintCommandHelp(string cmdStr, CommandRunner app, bool plain)
    {
        // Try command first
        if (!app.Commands.TryGetValue(cmdStr, out var cmd))
        {
            // If not a command, try statements
            var stmtInfo = EnumerateStatementHelp()
                .FirstOrDefault(s => s.Name.Equals(cmdStr, StringComparison.OrdinalIgnoreCase));

            if (stmtInfo.Name != null)
            {
                // Found a statement help entry
                PrintSingleStatementHelp(stmtInfo, plain);

                var stmtJson = new Dictionary<string, object>
                {
                    ["statement"] = stmtInfo.Name,
                    ["key"] = stmtInfo.Key,
                    ["description"] = stmtInfo.Description ?? string.Empty,
                    ["syntax"] = stmtInfo.Syntax ?? string.Empty,
                    ["example"] = stmtInfo.Example ?? string.Empty,
                };

                var jsonElementStmt = System.Text.Json.JsonSerializer.SerializeToElement(stmtJson);
                var stmtState = new CommandState
                {
                    Result = new ShellJson(jsonElementStmt),
                    IsPrinted = true,
                };
                return stmtState;
            }

            // Neither command nor statement
            AnsiConsole.Markup($"[{Theme.ErrorColorName}]{MessageService.GetString("error")}[/] ");
            ShellInterpreter.WriteLine(MessageService.GetString("error-command-not-found", MessageService.Args("command", cmdStr)));
            return new ErrorCommandState(new CommandException("help", cmdStr + " not found."));
        }

        if (cmd == null)
        {
            return new CommandState();
        }

        if (plain)
        {
            // Plain output (no colors, no panels)
            if (!string.IsNullOrEmpty(cmd.Description))
            {
                ShellInterpreter.WriteLine(cmd.Description);
            }

            ShellInterpreter.WriteLine();
            ShellInterpreter.WriteLine(MessageService.GetString("help-usage", new System.Collections.Generic.Dictionary<string, object> { ["command"] = cmd.CommandName }) + " " + BuildPlainUsage(cmd));
            ShellInterpreter.WriteLine();

            if (cmd.Aliases.Count > 0)
            {
                ShellInterpreter.WriteLine($"{MessageService.GetString("help-aliases")} {string.Join(", ", cmd.Aliases)}");
                ShellInterpreter.WriteLine();
            }

            if (cmd.Parameters.Count > 0)
            {
                ShellInterpreter.WriteLine(MessageService.GetString("help-arguments"));
                foreach (var p in cmd.Parameters)
                {
                    var name = p.Name.FirstOrDefault() ?? string.Empty;
                    ShellInterpreter.Write("  ");
                    ShellInterpreter.Write(p.IsRequired ? name : $"[{name}]");
                    ShellInterpreter.Write(" ");
                    var desc = p.GetDescription(cmd.CommandName) ?? string.Empty;
                    if (!p.IsRequired)
                    {
                        ShellInterpreter.Write($"{MessageService.GetString("help-optional")} ");
                    }

                    ShellInterpreter.WriteLine(desc);
                }

                ShellInterpreter.WriteLine();
            }

            if (cmd.Options.Count > 0)
            {
                ShellInterpreter.WriteLine(MessageService.GetString("help-options"));
                foreach (var opt in cmd.Options)
                {
                    var names = string.Join(", ", opt.Name.Select(n => "-" + n));
                    var desc = opt.GetDescription(cmd.CommandName) ?? string.Empty;
                    ShellInterpreter.WriteLine($"  {names} {desc}");
                }

                ShellInterpreter.WriteLine();
            }

            // Use shared examples collection to avoid shadowing
            var plainExamples = cmd.ExamplesWithDescriptions;
            if (plainExamples.Count > 0)
            {
                ShellInterpreter.WriteLine(MessageService.GetString("help-examples"));
                for (int i = 0; i < plainExamples.Count; i++)
                {
                    var (ex, desc) = plainExamples[i];
                    if (!string.IsNullOrEmpty(desc))
                    {
                        ShellInterpreter.WriteLine($"  {i + 1}. {desc}");
                        ShellInterpreter.WriteLine($"     {ex}");
                    }
                    else
                    {
                        ShellInterpreter.WriteLine($"  {i + 1}. {ex}");
                    }
                }
            }
        }
        else
        {
            // Styled output
            if (!string.IsNullOrEmpty(cmd.Description))
            {
                AnsiConsole.MarkupLine($"{INDENT}{Theme.FormatHelpHeader(cmd.Description)}");
            }

            if (cmd.Aliases.Count > 0)
            {
                AnsiConsole.MarkupLine($"{INDENT}[dim]{Markup.Escape(MessageService.GetString("help-aliases"))} {Markup.Escape(string.Join(", ", cmd.Aliases))}[/]");
            }

            ShellInterpreter.WriteLine();
            WriteSectionHeader(MessageService.GetString("help-usage-heading"));
            AnsiConsole.Markup(INDENT + $"{Theme.CommandColor}{Markup.Escape(cmd.CommandName)}[/] ");
        }

        if (!plain && cmd?.Options != null)
        {
            foreach (var p in cmd.Options)
            {
                AnsiConsole.Markup(INDENT + "[[" + Theme.FormatHelpName("-" + (p.Name.FirstOrDefault() ?? string.Empty)));

                if (!p.PropertyInfo.PropertyType.IsAssignableFrom(typeof(bool)))
                {
                    AnsiConsole.Markup($" [dim]{MessageService.GetString("help-arg")}[/]");
                }

                AnsiConsole.Markup("]] ");
            }
        }

        if (!plain && cmd?.Parameters != null)
        {
            foreach (var p in cmd.Parameters)
            {
                var name = p.Name.FirstOrDefault();
                if (name == null)
                {
                    continue;
                }

                if (p.IsRequired)
                {
                    AnsiConsole.Markup(INDENT + Theme.FormatHelpName(name) + " ");
                }
                else
                {
                    AnsiConsole.Markup(INDENT + "[[" + Theme.FormatHelpName(name) + "]] ");
                }
            }

            ShellInterpreter.WriteLine();
            ShellInterpreter.WriteLine();

            if (cmd.Parameters.Count > 0)
            {
                WriteSectionHeader(MessageService.GetString("help-arguments-heading"));

                var table = new Table()
                    .Border(TableBorder.None)
                    .HideHeaders()
                    .AddColumn(new TableColumn("Name").Width(ARGPADDING))
                    .AddColumn(new TableColumn("Description"));

                foreach (var p in cmd.Parameters)
                {
                    var paramName = p.Name.FirstOrDefault() ?? string.Empty;
                    var nameDisplay = !p.IsRequired
                        ? "[[" + Theme.FormatHelpName(paramName) + "]]"
                        : Theme.FormatHelpName(paramName);

                    var descDisplay = string.Empty;
                    if (!p.IsRequired)
                    {
                        descDisplay += $"[italic dim]{MessageService.GetString("help-optional")}[/] ";
                    }

                    var argHelp = p.GetDescription(cmd.CommandName);
                    if (!string.IsNullOrEmpty(argHelp))
                    {
                        descDisplay += Theme.FormatHelpDescription(argHelp);
                    }
                    else
                    {
                        descDisplay += $"[{Theme.ErrorColorName}]{MessageService.GetString("error")}[/] {Theme.FormatHelpDescription(MessageService.GetString("help-description-not-found"))}";
                    }

                    table.AddRow(INDENT + nameDisplay, descDisplay);
                }

                AnsiConsole.Write(table);
                ShellInterpreter.WriteLine();
            }
        }

        if (!plain && cmd?.Options.Count > 0)
        {
            WriteSectionHeader(MessageService.GetString("help-options-heading"));

            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn("Name").Width(ARGPADDING))
                .AddColumn(new TableColumn("Description"));

            foreach (var p in cmd.Options)
            {
                StringBuilder sb = new();
                foreach (var n in p.Name)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append('-');
                    sb.Append(n);
                }

                var argHelp = p.GetDescription(cmd.CommandName);
                var descDisplay = !string.IsNullOrEmpty(argHelp)
                    ? Theme.FormatHelpDescription(argHelp)
                    : $"[{Theme.ErrorColorName}]{MessageService.GetString("error")}[/] {Theme.FormatHelpDescription(MessageService.GetString("help-description-not-found"))}";

                table.AddRow(INDENT + Theme.FormatHelpName(sb.ToString()), descDisplay);
            }

            AnsiConsole.Write(table);
        }

        var examples = cmd?.ExamplesWithDescriptions;
        if (!plain && examples != null && examples.Count > 0)
        {
            ShellInterpreter.WriteLine();
            WriteSectionHeader(MessageService.GetString("help-examples-heading"));
            for (int i = 0; i < examples.Count; i++)
            {
                var (example, description) = examples[i];
                if (!string.IsNullOrWhiteSpace(description))
                {
                    AnsiConsole.MarkupLine(INDENT + $"[{Theme.HelpAccentColorName}]\u25b6[/] {i + 1}. {Theme.FormatHelpDescription(description)}");
                }
                else
                {
                    AnsiConsole.MarkupLine(INDENT + $"[{Theme.HelpAccentColorName}]\u25b6[/] {i + 1}.");
                }

                var parser = new StatementParser(example);
                Statement? statement = null;
                try
                {
                    statement = parser.ParseStatement();
#pragma warning disable CZ0001 // Empty Catch Clause
                }
                catch
                {
                    // Ignore parse errors for highlighting purposes
                }
#pragma warning restore CZ0001 // Empty Catch Clause

                string highlighted;
                if (statement != null)
                {
                    var highlighter = new HighlightingVisitor(example, ShellInterpreter.Instance);
                    statement.Accept(highlighter);
                    highlighted = highlighter.GetResult();
                }
                else
                {
                    highlighted = Markup.Escape(example);
                }

                var panel = new Panel(highlighted)
                {
                    Border = BoxBorder.None,
                    Padding = new Padding(4, 0, 0, 0),
                };
                AnsiConsole.Write(panel);

                if (i < examples.Count - 1)
                {
                    ShellInterpreter.WriteLine();
                }
            }
        }

        var commandState = new CommandState();

        var helpJson = new Dictionary<string, object>
        {
            ["command"] = cmd?.CommandName ?? string.Empty,
            ["description"] = cmd?.Description ?? string.Empty,
            ["aliases"] = cmd?.Aliases.ToList() ?? [],
            ["additionalDescriptionForMcp"] = cmd?.McpDescription ?? string.Empty,
        };

        var parameters = new List<Dictionary<string, object>>();
        if (cmd != null)
        {
            foreach (var p in cmd.Parameters)
            {
                var paramInfo = new Dictionary<string, object>
                {
                    ["name"] = p.Name.FirstOrDefault() ?? string.Empty,
                    ["description"] = p.GetDescription(cmd.CommandName) ?? string.Empty,
                    ["required"] = p.IsRequired,
                };
                parameters.Add(paramInfo);
            }
        }

        helpJson["parameters"] = parameters;

        var options = new List<Dictionary<string, object>>();
        if (cmd != null)
        {
            foreach (var opt in cmd.Options)
            {
                var optInfo = new Dictionary<string, object>
                {
                    ["names"] = opt.Name,
                    ["description"] = opt.GetDescription(cmd.CommandName) ?? string.Empty,
                };
                options.Add(optInfo);
            }
        }

        helpJson["options"] = options;

        var ann = cmd?.McpAnnotation;
        if (ann != null && ann.Restricted)
        {
            helpJson["isRestricted"] = "This tool can't be run via MCP. User only.";
        }

        // Add statements JSON (for parity)
        var statementsJson = new List<Dictionary<string, object>>();
        foreach (var info in EnumerateStatementHelp())
        {
            statementsJson.Add(new Dictionary<string, object>
            {
                ["name"] = info.Name,
                ["key"] = info.Key,
                ["description"] = info.Description ?? string.Empty,
                ["syntax"] = info.Syntax ?? string.Empty,
                ["example"] = info.Example ?? string.Empty,
            });
        }

        helpJson["statements"] = statementsJson;

        var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(helpJson);
        commandState.Result = new ShellJson(jsonElement);
        commandState.IsPrinted = true;
        return commandState;
    }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var app = ShellInterpreter.Instance.App;

        if (!string.IsNullOrEmpty(this.Command))
        {
            var state = HelpCommand.PrintCommandHelp(this.Command, app, this.Plain);
            return Task.FromResult(state);
        }

        if (this.Plain)
        {
            ShellInterpreter.WriteLine(MessageService.GetString("help-available-commands"));
            ShellInterpreter.WriteLine();
        }
        else
        {
            // Create a nice header for the commands list
            var headerPanel = new Panel(Theme.FormatHelpHeader(MessageService.GetString("help-available-commands")))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = Style.Parse("green"),
                Padding = new Padding(1, 0, 1, 0),
            };
            AnsiConsole.Write(headerPanel);
            ShellInterpreter.WriteLine();
        }

        // Group commands by category (basic heuristic based on name patterns)
        var connectionCmds = new List<CommandFactory>();
        var dataCmds = new List<CommandFactory>();
        var managementCmds = new List<CommandFactory>();
        var utilityCmds = new List<CommandFactory>();

        foreach (var cmd in EnumeratePrimaryCommands(app))
        {
            if (this.Details)
            {
                HelpCommand.PrintCommandHelp(cmd.CommandName, app, this.Plain);
                continue;
            }

            // Categorize commands
            if (cmd.CommandName.Contains("connect") || cmd.CommandName == "disconnect")
            {
                connectionCmds.Add(cmd);
            }
            else if (cmd.CommandName == "query" || cmd.CommandName == "print" || cmd.CommandName.Contains("item") || cmd.CommandName == "rm")
            {
                dataCmds.Add(cmd);
            }
            else if (cmd.CommandName.StartsWith("mk") || cmd.CommandName.StartsWith("rm") || cmd.CommandName == "create" || cmd.CommandName == "delete")
            {
                managementCmds.Add(cmd);
            }
            else
            {
                utilityCmds.Add(cmd);
            }
        }

        if (!this.Details)
        {
            if (this.Plain)
            {
                // Plain category listing
                PrintPlainCategory(MessageService.GetString("help-category-connection"), connectionCmds);
                PrintPlainCategory(MessageService.GetString("help-category-data-operations"), dataCmds);
                PrintPlainCategory(MessageService.GetString("help-category-management"), managementCmds);
                PrintPlainCategory(MessageService.GetString("help-category-utilities"), utilityCmds);
            }
            else
            {
                PrintCommandCategory(MessageService.GetString("help-category-connection-styled"), connectionCmds);
                PrintCommandCategory(MessageService.GetString("help-category-data-operations-styled"), dataCmds);
                PrintCommandCategory(MessageService.GetString("help-category-management-styled"), managementCmds);
                PrintCommandCategory(MessageService.GetString("help-category-utilities-styled"), utilityCmds);
            }
        }

        PrintStatementHelps(this.Plain);

        var allCommandsHelp = new Dictionary<string, object>
        {
            ["help"] = MessageService.GetString("help-list-of-available-commands"),
        };

        var commands = new List<Dictionary<string, object>>();
        foreach (var cmd in EnumeratePrimaryCommands(app))
        {
            var cmdInfo = new Dictionary<string, object>
            {
                ["command"] = cmd.CommandName,
                ["description"] = cmd.Description ?? string.Empty,
            };

            if (!string.IsNullOrEmpty(cmd.McpDescription))
            {
                cmdInfo["mcpDescription"] = cmd.McpDescription;
            }

            if (cmd.McpRestricted)
            {
                cmdInfo["mcpRestricted"] = true;
            }

            var parameterNames = cmd.Parameters.Select(p => p.Name.FirstOrDefault() ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (parameterNames.Count > 0)
            {
                cmdInfo["parameters"] = parameterNames;
            }

            var optionNames = cmd.Options.SelectMany(o => o.Name).ToList();
            if (optionNames.Count > 0)
            {
                cmdInfo["options"] = optionNames;
            }

            commands.Add(cmdInfo);
        }

        allCommandsHelp["commands"] = commands;

        var statementsJson = new List<Dictionary<string, object>>();
        foreach (var info in EnumerateStatementHelp())
        {
            statementsJson.Add(new Dictionary<string, object>
            {
                ["name"] = info.Name,
                ["key"] = info.Key,
                ["description"] = info.Description ?? string.Empty,
                ["syntax"] = info.Syntax ?? string.Empty,
                ["example"] = info.Example ?? string.Empty,
            });
        }

        allCommandsHelp["statements"] = statementsJson;

        var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(allCommandsHelp);
        commandState.Result = new ShellJson(jsonElement);
        commandState.IsPrinted = true;

        return Task.FromResult(commandState);
    }

    private static IEnumerable<CommandFactory> EnumeratePrimaryCommands(CommandRunner app)
    {
        return app.Commands.Values
            .DistinctBy(c => c.CommandName)
            .OrderBy(c => c.CommandName);
    }

    private static void PrintCommandCategory(string categoryName, List<CommandFactory> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        AnsiConsole.MarkupLine(INDENT + Theme.FormatHelpHeader(categoryName));
        ShellInterpreter.WriteLine();

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Command").Width(20))
            .AddColumn(new TableColumn("Description"));

        foreach (var cmd in commands)
        {
            table.AddRow(INDENT + $"{Theme.CommandColor}{cmd.CommandName}[/]", Theme.FormatHelpDescription(cmd.Description ?? string.Empty));
        }

        AnsiConsole.Write(table);
        ShellInterpreter.WriteLine();
    }

    private static void PrintSingleStatementHelp((string Name, string Key, string? Description, string? Syntax, string? Example) s, bool plain = false)
    {
        if (plain)
        {
            // Plain text output
            if (!string.IsNullOrWhiteSpace(s.Description))
            {
                ShellInterpreter.WriteLine(s.Description);
            }

            ShellInterpreter.WriteLine();

            if (!string.IsNullOrWhiteSpace(s.Syntax))
            {
                ShellInterpreter.WriteLine(MessageService.GetString("help-syntax") + ":");
                ShellInterpreter.WriteLine($"  {s.Syntax}");
                ShellInterpreter.WriteLine();
            }

            if (!string.IsNullOrWhiteSpace(s.Example))
            {
                ShellInterpreter.WriteLine(MessageService.GetString("help-example") + ":");
                var lines = s.Example.Split('\n');
                foreach (var l in lines)
                {
                    if (string.IsNullOrWhiteSpace(l))
                    {
                        continue;
                    }

                    ShellInterpreter.WriteLine($"  {l.Trim()}");
                }

                ShellInterpreter.WriteLine();
            }

            return;
        }

        // Use consistent styling with command help
        if (!string.IsNullOrWhiteSpace(s.Description))
        {
            AnsiConsole.MarkupLine($"{INDENT}{Theme.FormatHelpHeader(s.Description)}");
        }

        ShellInterpreter.WriteLine();

        if (!string.IsNullOrWhiteSpace(s.Syntax))
        {
            WriteSectionHeader(MessageService.GetString("help-syntax"));

            // Apply syntax highlighting: keywords in cyan, placeholders in yellow, optional parts in dim
            var highlighted = s.Syntax;

            // Escape the syntax first
            highlighted = Markup.Escape(highlighted);

            // Highlight keywords (if, else, while, do, for, in, loop, break, continue, return, def)
            highlighted = System.Text.RegularExpressions.Regex.Replace(highlighted, @"\b(if|else|while|do|for|in|loop|break|continue|return|def)\b", "[" + Theme.KeywordColorName + "]$1[/]");

            // Highlight placeholders <...>
            highlighted = System.Text.RegularExpressions.Regex.Replace(highlighted, @"&lt;([^&]+)&gt;", "[" + Theme.HelpPlaceholderColorName + "]<$1>[/]");

            // Highlight optional syntax [[[ ]]]
            highlighted = System.Text.RegularExpressions.Regex.Replace(highlighted, @"\[\[\[", "[dim][[[/]");
            highlighted = System.Text.RegularExpressions.Regex.Replace(highlighted, @"\]\]\]", "[dim]]][/]");

            // Highlight $ for variables
            highlighted = highlighted.Replace("$", "[" + Theme.HelpVariableColorName + "]$[/]");
            AnsiConsole.MarkupLine($"{INDENT}{highlighted}");
            ShellInterpreter.WriteLine();
        }

        if (!string.IsNullOrWhiteSpace(s.Example))
        {
            WriteSectionHeader(MessageService.GetString("help-example"));
            var parser = new StatementParser(s.Example);
            Statement? statement = null;
            try
            {
                statement = parser.ParseStatement();
#pragma warning disable CZ0001 // Empty Catch Clause
            }
            catch
            {
                // Ignore parse errors for highlighting purposes
            }
#pragma warning restore CZ0001 // Empty Catch Clause

            string highlighted;
            if (statement != null)
            {
                var highlighter = new HighlightingVisitor(s.Example, ShellInterpreter.Instance);
                statement.Accept(highlighter);
                var result = highlighter.GetResult();
                highlighted = result;
            }
            else
            {
                highlighted = Markup.Escape(s.Example);
            }

            var lines = highlighted.Split('\n');
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l))
                {
                    continue;
                }

                var panel = new Panel(l)
                {
                    Border = BoxBorder.None,
                    Padding = new Padding(2, 0, 0, 0),
                };
                AnsiConsole.Write(panel);
            }

            ShellInterpreter.WriteLine();
        }
    }

    private static void PrintStatementHelps()
    {
        var statements = EnumerateStatementHelp().ToList();
        if (statements.Count == 0)
        {
            return;
        }

        var stmtPanel = new Panel($"[bold]Control Flow Statements[/]")
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green"),
            Padding = new Padding(1, 0, 1, 0),
        };
        AnsiConsole.Write(stmtPanel);
        ShellInterpreter.WriteLine();

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Statement").Width(15))
            .AddColumn(new TableColumn("Description"));

        foreach (var s in statements.OrderBy(s => s.Name))
        {
            var desc = !string.IsNullOrWhiteSpace(s.Description)
                ? Theme.FormatHelpDescription(s.Description)
                : string.Empty;

            table.AddRow(INDENT + Theme.FormatKeyword(s.Name), desc);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(string.Empty);
    }

    private static void PrintStatementHelps(bool plain)
    {
        var statements = EnumerateStatementHelp().ToList();
        if (statements.Count == 0)
        {
            return;
        }

        if (plain)
        {
            ShellInterpreter.WriteLine(MessageService.GetString("help-control-flow-statements") + ":");
            ShellInterpreter.WriteLine();
            foreach (var s in statements.OrderBy(s => s.Name))
            {
                ShellInterpreter.WriteLine($"  {s.Name}");
                if (!string.IsNullOrWhiteSpace(s.Description))
                {
                    ShellInterpreter.WriteLine($"    {s.Description}");
                }

                ShellInterpreter.WriteLine();
            }
        }
        else
        {
            var stmtPanel = new Panel(Theme.FormatHelpHeader(MessageService.GetString("help-control-flow-statements")))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = Style.Parse("green"),
                Padding = new Padding(1, 0, 1, 0),
            };
            AnsiConsole.Write(stmtPanel);
            ShellInterpreter.WriteLine();

            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn("Statement").Width(15))
                .AddColumn(new TableColumn("Description"));

            foreach (var s in statements.OrderBy(s => s.Name))
            {
                var desc = !string.IsNullOrWhiteSpace(s.Description)
                    ? Theme.FormatHelpDescription(s.Description)
                    : string.Empty;

                table.AddRow(INDENT + Theme.FormatKeyword(s.Name), desc);
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine(string.Empty);
        }
    }

    private static IEnumerable<(string Name, string Key, string? Description, string? Syntax, string? Example)> EnumerateStatementHelp()
    {
        var stmtType = typeof(Statement);
        var asm = stmtType.Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (!stmtType.IsAssignableFrom(t) || t.IsAbstract)
            {
                continue;
            }

            var helpAttr = t.GetCustomAttribute<AstHelpAttribute>();
            if (helpAttr == null)
            {
                continue;
            }

            var key = helpAttr.Key;
            var name = key.StartsWith("statement-", StringComparison.OrdinalIgnoreCase)
                ? key.Substring("statement-".Length)
                : key;

            string? GetStr(string suffix)
            {
                try
                {
                    return MessageService.GetString(key + suffix);
                }
                catch
                {
                    return null;
                }
            }

            var description = GetStr("-description");
            var syntax = GetStr("-syntax");
            var example = GetStr("-example");

            yield return (name, key, description, syntax, example);
        }
    }

    private static void WriteSectionHeader(string title)
    {
        var rule = new Rule(Theme.FormatHelpHeader(title))
        {
            Justification = Justify.Left,
            Style = Style.Parse("grey"),
        };
        AnsiConsole.Write(rule);
    }

    private static string BuildPlainUsage(CommandFactory cmd)
    {
        var sb = new StringBuilder();
        foreach (var opt in cmd.Options)
        {
            var primary = opt.Name.FirstOrDefault();
            if (primary == null)
            {
                continue;
            }

            sb.Append("[-" + primary);
            if (!opt.PropertyInfo.PropertyType.IsAssignableFrom(typeof(bool)))
            {
                sb.Append(" <ARG>");
            }

            sb.Append("] ");
        }

        foreach (var p in cmd.Parameters)
        {
            var name = p.Name.FirstOrDefault();
            if (name == null)
            {
                continue;
            }

            sb.Append(p.IsRequired ? name + " " : "[" + name + "] ");
        }

        return sb.ToString().TrimEnd();
    }

    private static void PrintPlainCategory(string title, List<CommandFactory> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        ShellInterpreter.WriteLine(title + ":");
        foreach (var c in commands)
        {
            ShellInterpreter.WriteLine("  " + c.CommandName + " - " + (c.Description ?? string.Empty));
        }

        ShellInterpreter.WriteLine();
    }
}