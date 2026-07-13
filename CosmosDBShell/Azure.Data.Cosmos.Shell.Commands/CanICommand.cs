// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[CosmosCommand("can-i")]
[CosmosExample("can-i read", Description = "Probe whether the current identity can read items in the current container")]
[CosmosExample("can-i query --database=MyDB --container=Products", Description = "Probe query access against a specific container")]
[CosmosExample("can-i write", Description = "Probe write access using a safe, non-mutating operation")]
[CosmosExample("can-i read --format=json", Description = "Emit the access check as a JSON object")]
[McpAnnotation(
    Description = @"
Probes whether the current identity can perform an action against a container without mutating data.

Actions: read, query, write, manage. The probe issues a safe, non-mutating data-plane request (a point read of a random id,
a COUNT query, or a delete of a random non-existent id) and reports allow, deny, or indeterminate based on the response.

The 'write' result is a heuristic derived from delete permission. The 'manage' action cannot be probed on the data plane and is
reported as indeterminate. Account-key and emulator connections use a master key and are reported as allow. Use --format
(table, json, csv) to control the output.")]
internal class CanICommand : CosmosCommand
{
    [CosmosParameter("action")]
    public string Action { get; init; } = string.Empty;

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("format", "f")]
    public string? OutputFormat { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("can-i");
        }

        var action = (this.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action is not ("read" or "query" or "write" or "manage"))
        {
            throw new CommandException("can-i", MessageService.GetArgsString("command-can-i-invalid-action", "action", this.Action ?? string.Empty));
        }

        var format = this.OutputFormat ?? Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT");
        commandState.SetFormat(format);
        bool render = IsTableFormat(format) && string.IsNullOrEmpty(shell.StdOutRedirect);

        string? databaseName = this.Database;
        string? containerName = this.Container;
        switch (connectedState)
        {
            case ContainerState containerState:
                databaseName ??= containerState.DatabaseName;
                containerName ??= containerState.ContainerName;
                break;
            case DatabaseState databaseState:
                databaseName ??= databaseState.DatabaseName;
                break;
        }

        // 'manage' maps to DDL / control-plane actions that cannot be probed without a
        // mutating or control-plane operation, so it is always reported as indeterminate.
        if (action == "manage")
        {
            return this.Build(shell, commandState, action, databaseName, containerName, "indeterminate", "none", null, MessageService.GetString("command-can-i-manage-note"), render);
        }

        if (string.IsNullOrEmpty(databaseName) || string.IsNullOrEmpty(containerName))
        {
            throw new CommandException("can-i", MessageService.GetString("command-can-i-requires-container"));
        }

        // Account-key and emulator connections use a master key, which grants full access.
        if (shell.ActiveCredential is null)
        {
            return this.Build(shell, commandState, action, databaseName, containerName, "allow", "key", null, MessageService.GetString("command-can-i-key-note"), render);
        }

        var container = connectedState.Client.GetContainer(databaseName, containerName);

        HttpStatusCode statusCode;
        switch (action)
        {
            case "read":
                statusCode = await ProbeReadAsync(container, token);
                break;
            case "query":
                statusCode = await ProbeQueryAsync(container, token);
                break;
            default:
                statusCode = await ProbeWriteAsync(container, token);
                break;
        }

        var (decision, statusNote) = MapDecision(statusCode);
        string? note = statusNote;
        if (action == "write" && decision == "allow")
        {
            note = MessageService.GetString("command-can-i-write-heuristic-note");
        }

        return this.Build(shell, commandState, action, databaseName, containerName, decision, "probe", (int)statusCode, note, render);
    }

    private static async Task<HttpStatusCode> ProbeReadAsync(Container container, CancellationToken token)
    {
        using var response = await container.ReadItemStreamAsync(
            Guid.NewGuid().ToString(),
            new PartitionKey(Guid.NewGuid().ToString()),
            requestOptions: null,
            cancellationToken: token);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> ProbeQueryAsync(Container container, CancellationToken token)
    {
        using var iterator = container.GetItemQueryStreamIterator(new QueryDefinition("SELECT VALUE COUNT(1) FROM c"));
        using var response = await iterator.ReadNextAsync(token);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> ProbeWriteAsync(Container container, CancellationToken token)
    {
        // Deleting a random, almost-certainly-nonexistent id is non-mutating: an authorized
        // caller gets 404 NotFound, an unauthorized caller gets 403 Forbidden. The bogus
        // If-Match ETag guarantees the probe never mutates data: even in the vanishingly
        // unlikely event that the random id collides with an existing item, the delete fails
        // with 412 PreconditionFailed (still treated as allow) instead of removing the item.
        var requestOptions = new ItemRequestOptions { IfMatchEtag = "\"cosmosdb-shell-can-i-probe\"" };
        using var response = await container.DeleteItemStreamAsync(
            Guid.NewGuid().ToString(),
            new PartitionKey(Guid.NewGuid().ToString()),
            requestOptions: requestOptions,
            cancellationToken: token);
        return response.StatusCode;
    }

    private static (string Decision, string? Note) MapDecision(HttpStatusCode statusCode)
    {
        switch (statusCode)
        {
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.Unauthorized:
                return ("deny", null);
            case HttpStatusCode.OK:
            case HttpStatusCode.NoContent:
            case HttpStatusCode.NotFound:
            case HttpStatusCode.PreconditionFailed:
                return ("allow", null);
            case HttpStatusCode.TooManyRequests:
                return ("allow", MessageService.GetString("command-can-i-throttled-note"));
            default:
                return ("indeterminate", MessageService.GetArgsString("command-can-i-unexpected-status", "status", ((int)statusCode).ToString()));
        }
    }

    private static string BuildScope(string databaseName, string? containerName)
    {
        return string.IsNullOrEmpty(containerName) ? $"/{databaseName}" : $"/{databaseName}/{containerName}";
    }

    private static bool IsTableFormat(string? format)
    {
        return string.IsNullOrEmpty(format)
            || string.Equals(format, "table", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "tbl", StringComparison.OrdinalIgnoreCase);
    }

    private CommandState Build(
        ShellInterpreter shell,
        CommandState commandState,
        string action,
        string? databaseName,
        string? containerName,
        string decision,
        string method,
        int? statusCode,
        string? note,
        bool render)
    {
        var result = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["database"] = databaseName,
            ["container"] = containerName,
            ["decision"] = decision,
            ["method"] = method,
            ["statusCode"] = statusCode,
            ["note"] = note,
        };

        if (render)
        {
            this.RenderTable(action, databaseName, containerName, decision, method, statusCode, note);
        }

        commandState.IsPrinted = render;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(result));
        return commandState;
    }

    private void RenderTable(string action, string? databaseName, string? containerName, string decision, string method, int? statusCode, string? note)
    {
        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-can-i-title")));

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty);
        table.HideHeaders();

        table.AddRow(MessageService.GetString("command-can-i-action"), Theme.FormatTableValue(action));
        if (!string.IsNullOrEmpty(databaseName))
        {
            table.AddRow(MessageService.GetString("command-can-i-scope"), Theme.FormatTableValue(BuildScope(databaseName, containerName)));
        }

        table.AddRow(MessageService.GetString("command-can-i-decision"), Theme.FormatTableValue(decision));
        table.AddRow(MessageService.GetString("command-can-i-method"), Theme.FormatTableValue(method));
        if (statusCode is { } code)
        {
            table.AddRow(MessageService.GetString("command-can-i-status"), Theme.FormatTableValue(code.ToString()));
        }

        AnsiConsole.Write(table);

        if (!string.IsNullOrEmpty(note))
        {
            AnsiConsole.MarkupLine(Theme.FormatMuted(note));
        }
    }
}
