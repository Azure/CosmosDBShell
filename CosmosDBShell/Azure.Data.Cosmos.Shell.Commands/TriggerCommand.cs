//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos.Scripts;
using Spectre.Console;

[CosmosCommand("trigger")]
[CosmosExample("trigger list", Description = "List the triggers in the current container")]
[CosmosExample("trigger show myTrigger", Description = "Display the body of a trigger")]
[CosmosExample("trigger create myTrigger ./myTrigger.js --type pre --operation create", Description = "Create a pre-trigger for create operations")]
[CosmosExample("trigger create myTrigger ./myTrigger.js --type post --operation all --force", Description = "Create or replace a post-trigger for all operations")]
[CosmosExample("trigger delete myTrigger", Description = "Delete a trigger")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Triggers",
    Description = @"
Manages JavaScript triggers on the current Cosmos DB container through subcommands:
- 'list' returns the trigger ids in the container with their type and operation.
- 'show <name>' returns the body of a trigger.
- 'create <name> <file>' creates a trigger from a JavaScript file. --type selects pre or post, --operation selects all/create/replace/delete/update, and --force replaces an existing one.
- 'delete <name>' removes a trigger.
This command is restricted in MCP. Run it manually in the shell.
",
    Restricted = true,
    Destructive = true,
    OpenWorld = true)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class TriggerCommand : CosmosCommand
{
    [CosmosParameter("subcommand", RequiredErrorKey = "command-trigger-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    [CosmosParameter("value", IsRequired = false)]
    public string? Value { get; init; }

    [CosmosOption("type", "t")]
    public string? Type { get; init; }

    [CosmosOption("operation", "op")]
    public string? Operation { get; init; }

    [CosmosOption("force", "f")]
    public bool? Force { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    /// <summary>
    /// Normalizes a subcommand token to its canonical lower-case form.
    /// </summary>
    internal static string NormalizeSubcommand(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Parses the <c>--type</c> option into a <see cref="TriggerType"/>. Accepts
    /// 'pre' and 'post' (case-insensitive).
    /// </summary>
    internal static TriggerType ParseTriggerType(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "pre" => TriggerType.Pre,
            "post" => TriggerType.Post,
            _ => throw new CommandException(
                "trigger",
                MessageService.GetArgsString("command-trigger-error-invalid_type", "type", value ?? string.Empty)),
        };
    }

    /// <summary>
    /// Parses the <c>--operation</c> option into a <see cref="TriggerOperation"/>.
    /// Defaults to <see cref="TriggerOperation.All"/> when no value is supplied.
    /// </summary>
    internal static TriggerOperation ParseTriggerOperation(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "all" => TriggerOperation.All,
            "create" => TriggerOperation.Create,
            "replace" => TriggerOperation.Replace,
            "delete" => TriggerOperation.Delete,
            "update" => TriggerOperation.Update,
            _ => throw new CommandException(
                "trigger",
                MessageService.GetArgsString("command-trigger-error-invalid_operation", "operation", value ?? string.Empty)),
        };
    }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var subcommand = NormalizeSubcommand(this.Subcommand);
        if (subcommand.Length == 0)
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-missing_subcommand"));
        }

        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("trigger");
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "trigger",
            token);

        return subcommand switch
        {
            "list" or "ls" => await this.ListAsync(container, commandState, token),
            "show" or "cat" => await this.ShowAsync(container, commandState, token),
            "exists" => await this.ExistsAsync(container, commandState, token),
            "create" or "set" => await this.CreateAsync(container, commandState, token),
            "delete" or "rm" => await this.DeleteAsync(container, commandState, token),
            _ => throw new CommandException(
                "trigger",
                MessageService.GetArgsString("command-trigger-error-invalid_subcommand", "subcommand", subcommand)),
        };
    }

    private static CommandException NotFound(string name, Exception inner) =>
        new("trigger", MessageService.GetArgsString("command-trigger-error-not_found", "name", name), inner);

    private async Task<CommandState> ListAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var items = new List<object>();
        var rows = new List<(string Id, string Type, string Operation, int BodyLength)>();
        using var iterator = container.Scripts.GetTriggerQueryIterator<TriggerProperties>();
        while (iterator.HasMoreResults)
        {
            foreach (var properties in await iterator.ReadNextAsync(token))
            {
                items.Add(new
                {
                    id = properties.Id,
                    triggerType = properties.TriggerType.ToString(),
                    triggerOperation = properties.TriggerOperation.ToString(),
                    etag = properties.ETag,
                    bodyLength = properties.Body?.Length ?? 0,
                });
                rows.Add((
                    properties.Id,
                    properties.TriggerType.ToString(),
                    properties.TriggerOperation.ToString(),
                    properties.Body?.Length ?? 0));
            }
        }

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-trigger-list-empty")));
        }
        else
        {
            AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-title")));
            var table = new Table();
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-column-id"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-column-type"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-column-operation"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-column-size"))).RightAligned());

            foreach (var row in rows)
            {
                table.AddRow(
                    Theme.FormatTableValue(row.Id),
                    Theme.FormatTableValue(row.Type),
                    Theme.FormatTableValue(row.Operation),
                    Theme.FormatTableValue(row.BodyLength.ToString()));
            }

            AnsiConsole.Write(table);
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(items));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> ShowAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        try
        {
            var response = await container.Scripts.ReadTriggerAsync(name, cancellationToken: token);
            commandState.Result = new ShellText(response.Resource.Body);
            return commandState;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(name, ex);
        }
    }

    private async Task<CommandState> ExistsAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        bool exists;
        try
        {
            await container.Scripts.ReadTriggerAsync(name, cancellationToken: token);
            exists = true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            exists = false;
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            exists ? "command-trigger-exists-yes" : "command-trigger-exists-no",
            "name",
            name));

        commandState.Result = new ShellBool(exists);
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> CreateAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();
        bool force = this.Force == true;

        if (string.IsNullOrWhiteSpace(this.Type))
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-missing_type"));
        }

        var triggerType = ParseTriggerType(this.Type);
        var triggerOperation = ParseTriggerOperation(this.Operation);

        var body = this.TryReadExplicitBody(commandState)
            ?? throw new CommandException("trigger", MessageService.GetString("command-trigger-error-missing_file"));

        var properties = new TriggerProperties
        {
            Id = name,
            Body = body,
            TriggerType = triggerType,
            TriggerOperation = triggerOperation,
        };

        TriggerResponse response;
        bool replaced;
        if (force)
        {
            try
            {
                response = await container.Scripts.ReplaceTriggerAsync(properties, cancellationToken: token);
                replaced = true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                response = await container.Scripts.CreateTriggerAsync(properties, cancellationToken: token);
                replaced = false;
            }
        }
        else
        {
            try
            {
                response = await container.Scripts.CreateTriggerAsync(properties, cancellationToken: token);
                replaced = false;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                throw new CommandException(
                    "trigger",
                    MessageService.GetArgsString("command-trigger-error-already_exists", "name", name),
                    ex);
            }
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            replaced ? "command-trigger-replaced" : "command-trigger-created",
            "name",
            name,
            "charge",
            response.RequestCharge.ToString("F2")));

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            id = name,
            triggerType = triggerType.ToString(),
            triggerOperation = triggerOperation.ToString(),
        }));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> DeleteAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        try
        {
            var response = await container.Scripts.DeleteTriggerAsync(name, cancellationToken: token);
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-trigger-deleted",
                "name",
                name,
                "charge",
                response.RequestCharge.ToString("F2")));

            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, deleted = true }));
            commandState.IsPrinted = true;
            return commandState;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(name, ex);
        }
    }

    private string RequireName()
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-missing_name"));
        }

        return this.Name;
    }

    private string? TryReadExplicitBody(CommandState commandState)
    {
        if (!string.IsNullOrWhiteSpace(this.Value))
        {
            if (!File.Exists(this.Value))
            {
                throw new CommandException(
                    "trigger",
                    MessageService.GetArgsString("command-trigger-error-file_not_found", "file", this.Value));
            }

            return File.ReadAllText(this.Value);
        }

        var piped = commandState.Result?.ConvertShellObject(DataType.Text) as string;
        return string.IsNullOrEmpty(piped) ? null : piped;
    }
}
