// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

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
        var action = (this.Action ?? "list").Trim().ToLowerInvariant();
        if (action == "save")
        {
            return await this.RunSaveAsync(shell, commandState, token);
        }
        else if (action == "list")
        {
            return this.RunList(commandState);
        }
        else if (action == "use")
        {
            return await this.RunUseAsync(shell, commandState, token);
        }
        else if (action == "delete")
        {
            return this.RunDelete(commandState);
        }
        else
        {
            return this.RunUnknownAction(commandState, action);
        }
    }

    private async Task<CommandState> RunSaveAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        if (this.TryValidateName(this.Name, MessageService.GetString("command-profile-save-missing-name"), out var validationError))
        {
            AnsiConsole.MarkupLine(Theme.FormatError(validationError));
            return new ErrorCommandState(new CommandException("profile", validationError));
        }

        if (shell.State is not ConnectedState cs)
        {
            var msg = MessageService.GetString("command-profile-save-not-connected");
            AnsiConsole.MarkupLine(Theme.FormatError(msg));
            return new ErrorCommandState(new CommandException("profile", msg));
        }

        var profileName = this.Name!;

        // Only endpoint and connection mode are persisted; credentials are resolved
        // from the current auth context when the profile is used.
        var profile = new ConnectionProfile
        {
            Endpoint = cs.Client.Endpoint.ToString(),
            Mode = cs.Client.ClientOptions.ConnectionMode.ToString().ToLowerInvariant(),
        };

        ProfileManager.SaveProfile(profileName, profile);
        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-profile-saved", "name", profileName));
        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { saved = profileName }));
        return commandState;
    }

    private CommandState RunList(CommandState commandState)
    {
        var profiles = ProfileManager.ListProfiles();
        var table = new Table();
        table.AddColumn(MessageService.GetString("command-profile-list-col-name"));
        table.AddColumn(MessageService.GetString("command-profile-list-col-endpoint"));
        table.AddColumn(MessageService.GetString("command-profile-list-col-mode"));
        foreach (var kvp in profiles)
        {
            table.AddRow(
                Theme.FormatTableValue(kvp.Key),
                Theme.FormatTableValue(kvp.Value.Endpoint),
                Theme.FormatTableValue(kvp.Value.Mode ?? MessageService.GetString("command-profile-list-mode-default")));
        }

        AnsiConsole.Write(table);
        commandState.IsPrinted = true;
        var defaultModeLabel = MessageService.GetString("command-profile-list-mode-default");
        var shaped = new List<object>();
        foreach (var kvp in profiles)
        {
            shaped.Add(new
            {
                name = kvp.Key,
                endpoint = kvp.Value.Endpoint,
                mode = kvp.Value.Mode ?? defaultModeLabel,
            });
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { profiles = shaped }));
        return commandState;
    }

    private async Task<CommandState> RunUseAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        if (this.TryValidateName(this.Name, MessageService.GetString("command-profile-use-missing-name"), out var validationError))
        {
            AnsiConsole.MarkupLine(Theme.FormatError(validationError));
            return new ErrorCommandState(new CommandException("profile", validationError));
        }

        var profileName = this.Name!;
        var profile = ProfileManager.GetProfile(profileName);
        if (profile is null)
        {
            var msg = MessageService.GetArgsString("command-profile-unknown", "name", profileName);
            AnsiConsole.MarkupLine(Theme.FormatError(msg));
            return new ErrorCommandState(new CommandException("profile", msg));
        }

        // Delegate to ConnectCommand helper to perform the actual connection.
        var result = await ConnectCommand.ExecuteProfileAsync(profile, shell, token);
        return result;
    }

    private CommandState RunDelete(CommandState commandState)
    {
        if (this.TryValidateName(this.Name, MessageService.GetString("command-profile-delete-missing-name"), out var validationError))
        {
            AnsiConsole.MarkupLine(Theme.FormatError(validationError));
            return new ErrorCommandState(new CommandException("profile", validationError));
        }

        var profileName = this.Name!;
        if (!ProfileManager.DeleteProfile(profileName))
        {
            var notFound = MessageService.GetArgsString("command-profile-delete-not-found", "name", profileName);
            AnsiConsole.MarkupLine(Theme.FormatError(notFound));
            return new ErrorCommandState(new CommandException("profile", notFound));
        }

        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-profile-deleted", "name", profileName));
        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { deleted = profileName }));
        return commandState;
    }

    private CommandState RunUnknownAction(CommandState commandState, string action)
    {
        var msg = MessageService.GetArgsString("command-profile-unknown-action", "action", action);
        AnsiConsole.MarkupLine(Theme.FormatError(msg));
        return new ErrorCommandState(new CommandException("profile", msg));
    }

    private bool TryValidateName(string? name, string missingNameMessage, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = missingNameMessage;
            return true;
        }

        if (!NameRegex.IsMatch(name))
        {
            errorMessage = MessageService.GetArgsString("command-profile-invalid-name", "name", name);
            return true;
        }

        errorMessage = string.Empty;
        return false;
    }
}
