//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

/// <summary>
/// Inspects and switches the active color theme.
/// </summary>
[CosmosCommand("theme")]
[CosmosExample("theme", Description = "Show the active theme name")]
[CosmosExample("theme list", Description = "List all built-in themes")]
[CosmosExample("theme show", Description = "Print a sample of every role using the active theme")]
[CosmosExample("theme show light", Description = "Print a sample using the light theme without switching to it")]
[CosmosExample("theme use light", Description = "Switch the active theme for the rest of the session")]
internal class ThemeCommand : CosmosCommand
{
    [CosmosParameter("action", IsRequired = false)]
    public string? Action { get; init; }

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var action = (this.Action ?? "current").Trim().ToLowerInvariant();

        return action switch
        {
            "current" => Task.FromResult(this.RunCurrent(commandState)),
            "list" => Task.FromResult(this.RunList(commandState)),
            "show" => Task.FromResult(this.RunShow(commandState)),
            "use" or "set" => Task.FromResult(this.RunUse(commandState)),
            _ => Task.FromResult(this.RunUnknownAction(commandState, action)),
        };
    }

    private static CommandState ReportUnknownTheme(CommandState commandState, string requested)
    {
        var message = MessageService.GetArgsString(
            "command-theme-unknown",
            "name",
            requested,
            "themes",
            string.Join(", ", ThemeProfiles.All.Keys));
        AnsiConsole.MarkupLine(message);
        return new ErrorCommandState(new CommandException("theme", message));
    }

    private static string ResolveActiveName()
    {
        foreach (var (name, options) in ThemeProfiles.All)
        {
            if (ReferenceEquals(options, Theme.Current))
            {
                return name;
            }
        }

        return "custom";
    }

    private CommandState RunCurrent(CommandState commandState)
    {
        var name = ResolveActiveName();
        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-theme-active", "name", name));
        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { active = name }));
        return commandState;
    }

    private CommandState RunList(CommandState commandState)
    {
        var active = ResolveActiveName();
        var items = new List<Dictionary<string, object?>>();
        foreach (var name in ThemeProfiles.All.Keys)
        {
            var marker = string.Equals(name, active, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
            AnsiConsole.MarkupLine($"  {marker} {Markup.Escape(name)}");
            items.Add(new Dictionary<string, object?>
            {
                ["name"] = name,
                ["active"] = string.Equals(name, active, StringComparison.OrdinalIgnoreCase),
            });
        }

        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { themes = items }));
        return commandState;
    }

    private CommandState RunShow(CommandState commandState)
    {
        var profileName = string.IsNullOrWhiteSpace(this.Name) ? ResolveActiveName() : this.Name;
        if (!ThemeProfiles.TryGet(profileName, out var profile))
        {
            return ReportUnknownTheme(commandState, profileName);
        }

        var saved = Theme.Current;
        try
        {
            Theme.Apply(profile);
            AnsiConsole.MarkupLine(MessageService.GetArgsString("command-theme-sample-heading", "name", profileName.ToLowerInvariant()));

            var table = new Table().HideHeaders();
            table.AddColumn(string.Empty);
            table.AddColumn(string.Empty);
            void Row(string role, string sample) => table.AddRow(Markup.Escape(role), sample);

            Row("command", Theme.FormatCommand("connect"));
            Row("unknown command", Theme.FormatUnknownCommand("nope"));
            Row("argument name", Theme.FormatArgumentName("--max"));
            Row("connected prompt", Theme.ConnectedStatePromt("CS >"));
            Row("database name", Theme.DatabaseNamePromt("MyDb"));
            Row("container name", Theme.ContainerNamePromt("MyContainer"));
            Row("redirection", Theme.FormatRedirection(">>"));
            Row("redirection target", Theme.FormatRedirectionDestination("out.json"));
            Row("json property", Theme.FormatJsonProperty("\"id\""));
            Row("json punctuation", Theme.FormatJsonBracket(":"));
            Row("string literal", Theme.FormatStringLiteral("\"hello\""));
            Row("number literal", Theme.FormatNumberLiteral("42"));
            Row("keyword", Theme.FormatKeyword("if"));
            Row("operator", Theme.FormatOperator("+"));
            Row("error", Theme.FormatError("not found"));
            Row("warning", Theme.FormatWarning("retry?"));
            Row("muted", Theme.FormatMuted("2026-05-11"));
            Row("table value", Theme.FormatTableValue("West US"));
            Row("directory", Theme.FormatDirectory("docs"));
            Row("help header", Theme.FormatHelpHeader("Connection"));
            Row("help name", Theme.FormatHelpName("--theme"));
            Row("help description", Theme.FormatHelpDescription("Switches the active color theme."));
            Row("brackets", string.Concat(Theme.FormatBracket("{", 0), Theme.FormatBracket("[", 1), Theme.FormatBracket("(", 2), Theme.FormatBracket(")", 2), Theme.FormatBracket("]", 1), Theme.FormatBracket("}", 0)));

            AnsiConsole.Write(table);
        }
        finally
        {
            Theme.Apply(saved);
        }

        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { previewed = profileName.ToLowerInvariant() }));
        return commandState;
    }

    private CommandState RunUse(CommandState commandState)
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-theme-use-missing-name"));
            return new ErrorCommandState(new CommandException("theme", MessageService.GetString("command-theme-use-missing-name")));
        }

        if (!ThemeProfiles.TryGet(this.Name, out var profile))
        {
            return ReportUnknownTheme(commandState, this.Name);
        }

        Theme.Apply(profile);
        var name = this.Name.ToLowerInvariant();
        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-theme-applied", "name", name));
        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { applied = name }));
        return commandState;
    }

    private CommandState RunUnknownAction(CommandState commandState, string action)
    {
        var message = MessageService.GetArgsString(
            "command-theme-unknown-action",
            "action",
            action,
            "actions",
            "list, show, use");
        AnsiConsole.MarkupLine(message);
        return new ErrorCommandState(new CommandException("theme", message));
    }
}
