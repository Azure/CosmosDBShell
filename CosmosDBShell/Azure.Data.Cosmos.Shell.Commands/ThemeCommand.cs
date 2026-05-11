//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.IO;
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
[CosmosExample("theme list", Description = "List all available themes (built-in plus user files)")]
[CosmosExample("theme show", Description = "Print a sample of every role using the active theme")]
[CosmosExample("theme show light", Description = "Print a sample using the light theme without switching to it")]
[CosmosExample("theme use light", Description = "Switch the active theme for the rest of the session")]
[CosmosExample("theme load ./my-theme.toml", Description = "Load a theme from a TOML file and switch to it")]
[CosmosExample("theme save my-theme", Description = "Save the active theme as ~/.cosmosdbshell/themes/my-theme.toml")]
[CosmosExample("theme reload", Description = "Re-scan the user themes directory")]
internal class ThemeCommand : CosmosCommand
{
    [CosmosParameter("action", IsRequired = false)]
    public string? Action { get; init; }

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    [CosmosParameter("path", IsRequired = false)]
    public string? Path { get; init; }

    [CosmosOption("force", "f")]
    public bool Force { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var action = (this.Action ?? "current").Trim().ToLowerInvariant();

        return action switch
        {
            "current" => Task.FromResult(this.RunCurrent(commandState)),
            "list" => Task.FromResult(this.RunList(commandState)),
            "show" => Task.FromResult(this.RunShow(commandState)),
            "use" or "set" => Task.FromResult(this.RunUse(commandState)),
            "load" => Task.FromResult(this.RunLoad(commandState)),
            "save" => Task.FromResult(this.RunSave(commandState)),
            "reload" => Task.FromResult(this.RunReload(commandState)),
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
            string.Join(", ", ThemeRegistry.Instance.All.Keys));
        AnsiConsole.MarkupLine(message);
        return new ErrorCommandState(new CommandException("theme", message));
    }

    private static string ResolveActiveName()
    {
        foreach (var (name, registration) in ThemeRegistry.Instance.All)
        {
            if (ReferenceEquals(registration.Options, Theme.Current))
            {
                return name;
            }
        }

        return "custom";
    }

    /// <summary>
    /// Resolves a user-supplied theme reference. If the argument looks like a path
    /// (contains a directory separator or matches an existing file), it is treated
    /// as a file path. Otherwise it is resolved as a name in the user themes
    /// directory: <c>~/.cosmosdbshell/themes/&lt;name&gt;.toml</c>.
    /// </summary>
    private static string ResolveThemePath(string requested)
    {
        if (requested.Contains('/') || requested.Contains('\\') || File.Exists(requested))
        {
            return requested;
        }

        var withExtension = requested.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)
            ? requested
            : requested + ".toml";
        return System.IO.Path.Combine(ThemeFile.DefaultUserThemesDirectory(), withExtension);
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
        foreach (var (name, registration) in ThemeRegistry.Instance.All)
        {
            var marker = string.Equals(name, active, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
            var source = registration.Source switch
            {
                ThemeSource.BuiltIn => MessageService.GetString("command-theme-source-builtin"),
                ThemeSource.File => MessageService.GetArgsString("command-theme-source-file", "path", registration.Path ?? string.Empty),
                _ => string.Empty,
            };
            AnsiConsole.MarkupLine($"  {marker} {Markup.Escape(name)}  {Theme.FormatMuted(source)}");
            items.Add(new Dictionary<string, object?>
            {
                ["name"] = name,
                ["active"] = string.Equals(name, active, StringComparison.OrdinalIgnoreCase),
                ["source"] = registration.Source.ToString().ToLowerInvariant(),
                ["path"] = registration.Path,
                ["description"] = registration.Description,
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

    private CommandState RunLoad(CommandState commandState)
    {
        var requested = string.IsNullOrWhiteSpace(this.Name) ? this.Path : this.Name;
        if (string.IsNullOrWhiteSpace(requested))
        {
            var message = MessageService.GetString("command-theme-load-missing-path");
            AnsiConsole.MarkupLine(message);
            return new ErrorCommandState(new CommandException("theme", message));
        }

        var path = ResolveThemePath(requested);

        try
        {
            var result = ThemeRegistry.Instance.LoadFile(path);
            Theme.Apply(result.Options);
            AnsiConsole.MarkupLine(MessageService.GetArgsString(
                "command-theme-loaded",
                "name",
                result.Name,
                "path",
                result.Source));
            foreach (var warning in result.Warnings)
            {
                AnsiConsole.MarkupLine(Theme.FormatWarning(warning));
            }

            commandState.IsPrinted = true;
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                loaded = result.Name,
                path = result.Source,
                description = result.Description,
            }));
            return commandState;
        }
        catch (ThemeLoadException ex)
        {
            AnsiConsole.MarkupLine(Theme.FormatError(ex.Message));
            return new ErrorCommandState(new CommandException("theme", ex.Message));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            var message = MessageService.GetArgsString("command-theme-load-not-found", "path", path);
            AnsiConsole.MarkupLine(Theme.FormatError(message));
            return new ErrorCommandState(new CommandException("theme", message));
        }
    }

    private CommandState RunSave(CommandState commandState)
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            var message = MessageService.GetString("command-theme-save-missing-name");
            AnsiConsole.MarkupLine(message);
            return new ErrorCommandState(new CommandException("theme", message));
        }

        var path = string.IsNullOrWhiteSpace(this.Path)
            ? System.IO.Path.Combine(ThemeFile.DefaultUserThemesDirectory(), this.Name + ".toml")
            : this.Path;

        if (File.Exists(path) && !this.Force)
        {
            var message = MessageService.GetArgsString("command-theme-save-exists", "path", path);
            AnsiConsole.MarkupLine(Theme.FormatWarning(message));
            return new ErrorCommandState(new CommandException("theme", message));
        }

        try
        {
            ThemeFile.Save(this.Name, Theme.Current, path);
            AnsiConsole.MarkupLine(MessageService.GetArgsString(
                "command-theme-saved",
                "name",
                this.Name,
                "path",
                System.IO.Path.GetFullPath(path)));
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetArgsString("command-theme-save-hint-reload", "name", this.Name)));
            commandState.IsPrinted = true;
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                saved = this.Name,
                path = System.IO.Path.GetFullPath(path),
            }));
            return commandState;
        }
        catch (Exception ex)
        {
            var message = MessageService.GetArgsString("command-theme-save-failed", "path", path, "message", ex.Message);
            AnsiConsole.MarkupLine(Theme.FormatError(message));
            return new ErrorCommandState(new CommandException("theme", message, ex));
        }
    }

    private CommandState RunReload(CommandState commandState)
    {
        var registry = ThemeRegistry.Instance;
        registry.ResetToBuiltIns();
        var directory = ThemeFile.DefaultUserThemesDirectory();
        var loaded = registry.LoadFromDirectory(directory);

        AnsiConsole.MarkupLine(MessageService.GetArgsString(
            "command-theme-reloaded",
            "count",
            loaded,
            "directory",
            directory));
        foreach (var warning in registry.Warnings)
        {
            AnsiConsole.MarkupLine(Theme.FormatWarning(warning));
        }

        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            reloaded = loaded,
            directory,
            warnings = registry.Warnings,
        }));
        return commandState;
    }

    private CommandState RunUnknownAction(CommandState commandState, string action)
    {
        var message = MessageService.GetArgsString(
            "command-theme-unknown-action",
            "action",
            action,
            "actions",
            "list, show, use, load, save, reload");
        AnsiConsole.MarkupLine(message);
        return new ErrorCommandState(new CommandException("theme", message));
    }
}
