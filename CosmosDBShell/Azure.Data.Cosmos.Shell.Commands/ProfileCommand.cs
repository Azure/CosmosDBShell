using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;
using System.Text.Json;

namespace Azure.Data.Cosmos.Shell.Commands;

/// <summary>
/// Manage saved connection profiles.
/// </summary>
[CosmosCommand("profile")]
[CosmosExample("profile save dev", Description = "Save current connection as 'dev'")]
[CosmosExample("profile list", Description = "List saved profiles")]
[CosmosExample("profile use dev", Description = "Connect using saved profile 'dev'")]
[CosmosExample("profile delete dev", Description = "Delete saved profile 'dev'")]
internal class ProfileCommand : CosmosCommand
{
    private static readonly System.Text.RegularExpressions.Regex NameRegex =
        new System.Text.RegularExpressions.Regex("^[A-Za-z0-9_.-]{1,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    [CosmosParameter("action", IsRequired = false)]
    public string? Action { get; init; }

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var action = (this.Action ?? "current").Trim().ToLowerInvariant();
        if (action == "save")
        {
            return await RunSaveAsync(shell, commandState, token);
        }
        else if (action == "list")
        {
            return RunList(commandState);
        }
        else if (action == "use" || action == "set")
        {
            return await RunUseAsync(shell, commandState, token);
        }
        else if (action == "delete")
        {
            return RunDelete(commandState);
        }
        else
        {
            return RunUnknownAction(commandState, action);
        }
    }

    private async Task<CommandState> RunSaveAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-profile-save-missing-name"));
            return new ErrorCommandState(new CommandException("profile", MessageService.GetString("command-profile-save-missing-name")));
        }

        if (!NameRegex.IsMatch(this.Name))
        {
            var msg = MessageService.GetArgsString("command-profile-invalid-name", "name", this.Name);
            AnsiConsole.MarkupLine(Theme.FormatError(msg));
            return new ErrorCommandState(new CommandException("profile", msg));
        }

        if (shell.State is not ConnectedState cs)
        {
            var msg = MessageService.GetString("command-profile-save-not-connected");
            AnsiConsole.MarkupLine(msg);
            return new ErrorCommandState(new CommandException("profile", msg));
        }

        var profile = new ConnectionProfile
        {
            Endpoint = cs.Client.Endpoint.Host,
            Mode = cs.Client.ClientOptions.ConnectionMode.ToString().ToLowerInvariant(),
        };

        ProfileManager.SaveProfile(this.Name, profile);
        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-profile-saved", "name", this.Name));
        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { saved = this.Name }));
        return commandState;
    }

    private CommandState RunList(CommandState commandState)
    {
        var profiles = ProfileManager.ListProfiles();
        var table = new Table();
        table.AddColumn("Name");
        table.AddColumn("Endpoint");
        table.AddColumn("Mode");
        foreach (var kvp in profiles)
        {
            table.AddRow(kvp.Key, kvp.Value.Endpoint, kvp.Value.Mode ?? "default");
        }
        AnsiConsole.Write(table);
        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { profiles = profiles }));
        return commandState;
    }

    private async Task<CommandState> RunUseAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            var msg = MessageService.GetString("command-profile-use-missing-name");
            AnsiConsole.MarkupLine(msg);
            return new ErrorCommandState(new CommandException("profile", msg));
        }

        var profile = ProfileManager.GetProfile(this.Name);
        if (profile is null)
        {
            var msg = MessageService.GetArgsString("command-profile-unknown", "name", this.Name);
            AnsiConsole.MarkupLine(msg);
            return new ErrorCommandState(new CommandException("profile", msg));
        }

        // Delegate to ConnectCommand helper to perform the actual connection.
        var result = await ConnectCommand.ExecuteProfileAsync(profile, shell, token);
        return result;
    }

    private CommandState RunDelete(CommandState commandState)
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            var msg = MessageService.GetString("command-profile-delete-missing-name");
            AnsiConsole.MarkupLine(msg);
            return new ErrorCommandState(new CommandException("profile", msg));
        }

        ProfileManager.DeleteProfile(this.Name);
        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-profile-deleted", "name", this.Name));
        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { deleted = this.Name }));
        return commandState;
    }

    private CommandState RunUnknownAction(CommandState commandState, string action)
    {
        var msg = MessageService.GetArgsString("command-profile-unknown-action", "action", action);
        AnsiConsole.MarkupLine(msg);
        return new ErrorCommandState(new CommandException("profile", msg));
    }
}
