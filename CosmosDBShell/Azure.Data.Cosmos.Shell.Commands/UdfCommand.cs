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

[CosmosCommand("udf")]
[CosmosExample("udf list", Description = "List the user-defined functions in the current container")]
[CosmosExample("udf show myFunc", Description = "Display the body of a user-defined function")]
[CosmosExample("udf create myFunc ./myFunc.js", Description = "Create a user-defined function from a JavaScript file")]
[CosmosExample("udf create myFunc ./myFunc.js --force", Description = "Create or replace a user-defined function")]
[CosmosExample("udf delete myFunc", Description = "Delete a user-defined function")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "User-Defined Functions",
    Description = @"
Manages JavaScript user-defined functions (UDFs) on the current Cosmos DB container through subcommands:
- 'list' returns the user-defined function ids in the container.
- 'show <name>' returns the body of a user-defined function.
- 'create <name> <file>' creates a user-defined function from a JavaScript file. Pass --force to replace an existing one.
- 'delete <name>' removes a user-defined function.
This command is restricted in MCP. Run it manually in the shell.
",
    Restricted = true,
    Destructive = true,
    OpenWorld = true)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class UdfCommand : CosmosCommand
{
    [CosmosParameter("subcommand", RequiredErrorKey = "command-udf-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    [CosmosParameter("value", IsRequired = false)]
    public string? Value { get; init; }

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

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var subcommand = NormalizeSubcommand(this.Subcommand);
        if (subcommand.Length == 0)
        {
            throw new CommandException("udf", MessageService.GetString("command-udf-error-missing_subcommand"));
        }

        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("udf");
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "udf",
            token);

        return subcommand switch
        {
            "list" or "ls" => await this.ListAsync(container, commandState, token),
            "show" or "cat" => await this.ShowAsync(container, commandState, token),
            "exists" => await this.ExistsAsync(container, commandState, token),
            "create" or "set" => await this.CreateAsync(container, commandState, token),
            "delete" or "rm" => await this.DeleteAsync(container, commandState, token),
            _ => throw new CommandException(
                "udf",
                MessageService.GetArgsString("command-udf-error-invalid_subcommand", "subcommand", subcommand)),
        };
    }

    private static CommandException NotFound(string name, Exception inner) =>
        new("udf", MessageService.GetArgsString("command-udf-error-not_found", "name", name), inner);

    private async Task<CommandState> ListAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var items = new List<object>();
        var rows = new List<(string Id, int BodyLength)>();
        using var iterator = container.Scripts.GetUserDefinedFunctionQueryIterator<UserDefinedFunctionProperties>();
        while (iterator.HasMoreResults)
        {
            foreach (var properties in await iterator.ReadNextAsync(token))
            {
                items.Add(new
                {
                    id = properties.Id,
                    etag = properties.ETag,
                    bodyLength = properties.Body?.Length ?? 0,
                });
                rows.Add((
                    properties.Id,
                    properties.Body?.Length ?? 0));
            }
        }

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-udf-list-empty")));
        }
        else
        {
            AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-udf-list-title")));
            var table = new Table();
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-udf-list-column-id"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-udf-list-column-size"))).RightAligned());

            foreach (var row in rows)
            {
                table.AddRow(
                    Theme.FormatTableValue(row.Id),
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
            var response = await container.Scripts.ReadUserDefinedFunctionAsync(name, cancellationToken: token);
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
            await container.Scripts.ReadUserDefinedFunctionAsync(name, cancellationToken: token);
            exists = true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            exists = false;
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            exists ? "command-udf-exists-yes" : "command-udf-exists-no",
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

        var body = this.TryReadExplicitBody(commandState)
            ?? throw new CommandException("udf", MessageService.GetString("command-udf-error-missing_file"));

        var properties = new UserDefinedFunctionProperties { Id = name, Body = body };

        UserDefinedFunctionResponse response;
        bool replaced;
        if (force)
        {
            try
            {
                response = await container.Scripts.ReplaceUserDefinedFunctionAsync(properties, cancellationToken: token);
                replaced = true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                response = await container.Scripts.CreateUserDefinedFunctionAsync(properties, cancellationToken: token);
                replaced = false;
            }
        }
        else
        {
            try
            {
                response = await container.Scripts.CreateUserDefinedFunctionAsync(properties, cancellationToken: token);
                replaced = false;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                throw new CommandException(
                    "udf",
                    MessageService.GetArgsString("command-udf-error-already_exists", "name", name),
                    ex);
            }
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            replaced ? "command-udf-replaced" : "command-udf-created",
            "name",
            name,
            "charge",
            response.RequestCharge.ToString("F2")));

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name }));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> DeleteAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        try
        {
            var response = await container.Scripts.DeleteUserDefinedFunctionAsync(name, cancellationToken: token);
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-udf-deleted",
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
            throw new CommandException("udf", MessageService.GetString("command-udf-error-missing_name"));
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
                    "udf",
                    MessageService.GetArgsString("command-udf-error-file_not_found", "file", this.Value));
            }

            return File.ReadAllText(this.Value);
        }

        var piped = commandState.Result?.ConvertShellObject(DataType.Text) as string;
        return string.IsNullOrEmpty(piped) ? null : piped;
    }
}
