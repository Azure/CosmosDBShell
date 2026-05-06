//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

[CosmosCommand("settings")]
[CosmosExample("settings", Description = "Display account overview or container settings depending on context")]
[CosmosExample("settings --format=json", Description = "Display settings in JSON format")]
[CosmosExample("settings --database=MyDB --container=Products", Description = "Display container settings for a specific database and container")]
internal class SettingsCommand : CosmosCommand
{
    private static readonly Regex PrincipalIdRegex = new("Request for (.*) is blocked because principal \\[(.*)\\] does not have required RBAC permissions to perform action \\[(.*)\\]");

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("settings");
        }

        try
        {
            // Resolve database and container from options and current state
            string? databaseName = this.Database;
            string? containerName = this.Container;

            if (shell.State is ContainerState cs)
            {
                databaseName ??= cs.DatabaseName;
                containerName ??= cs.ContainerName;
            }
            else if (shell.State is DatabaseState ds)
            {
                databaseName ??= ds.DatabaseName;
            }

            // If both database and container are resolved, show container settings
            if (!string.IsNullOrEmpty(databaseName) && !string.IsNullOrEmpty(containerName))
            {
                return await ShowContainerSettingsAsync(connectedState, databaseName, containerName, commandState, token);
            }

            // Otherwise show account overview
            return await PrintOverviewAsync(connectedState.Client, commandState, token);
        }
        catch (Exception e)
        {
            if (TryGetPrincipialIdFromRbacException(e, out var id, out var request, out var permission))
            {
                AskForRBacPermissions(id ?? string.Empty, request ?? string.Empty, permission ?? string.Empty);
                return commandState;
            }

            throw new CommandException("settings", e);
        }
    }

    private static async Task<CommandState> ShowContainerSettingsAsync(ConnectedState state, string databaseName, string containerName, CommandState commandState, CancellationToken token)
    {
        var view = await CosmosResourceFacade.GetContainerSettingsAsync(state, databaseName, containerName, token);
        var mcpTable = new Dictionary<string, object?>();

        // Scale section - fail gracefully if it cannot be read
        AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-scale-heading")}[/]");

        AnsiConsole.Markup("\t");
        switch (view.Throughput)
        {
            case ThroughputAvailability.Available:
                var minDisplay = view.MinThroughput?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? MessageService.GetString("command-settings-na");
                var maxDisplay = view.MaxThroughput?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? MessageService.GetString("command-settings-na");
                AnsiConsole.MarkupLine(MessageService.GetArgsString("command-settings-scale-usage", "min", minDisplay, "max", maxDisplay));
                if (view.MinThroughput.HasValue)
                {
                    mcpTable["minThroughput"] = view.MinThroughput.Value;
                }

                if (view.MaxThroughput.HasValue)
                {
                    mcpTable["maxThroughput"] = view.MaxThroughput.Value;
                }

                break;
            case ThroughputAvailability.NotConfigured:
                AnsiConsole.MarkupLine($"[grey]{MessageService.GetString("command-settings-na")}[/]");
                break;
            default:
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(view.ThroughputErrorMessage ?? string.Empty)}[/]");
                break;
        }

        AnsiConsole.MarkupLine(string.Empty);

        mcpTable["id"] = view.ContainerName;
        mcpTable["partitionKey"] = view.PartitionKeyPaths;
        mcpTable["analyticalTTL"] = view.AnalyticalStorageTtl;

        AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-title")}[/]");

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty);

        string ttl;
        if (view.AnalyticalStorageTtl == null ||
            view.AnalyticalStorageTtl == 0)
        {
            ttl = MessageService.GetString("command-settings-Off");
        }
        else if (view.AnalyticalStorageTtl == -1)
        {
            ttl = MessageService.GetString("command-settings-On");
        }
        else
        {
            ttl = MessageService.GetArgsString(
                  "command-settings-ttl-seconds",
                  "seconds",
                  view.AnalyticalStorageTtl);
        }

        table.AddRow(MessageService.GetString("command-settings-ttl-label"), $"[white]{ttl}[/]");

        if (view.GeospatialType is { } geospatialType)
        {
            string geospatialLabel = string.Equals(geospatialType, "Geography", StringComparison.OrdinalIgnoreCase)
                ? MessageService.GetString("command-settings-geospatial-geography")
                : MessageService.GetString("command-settings-geospatial-geometry");
            table.AddRow(MessageService.GetString("command-settings-geospatial-label"), $"[white]{geospatialLabel}[/]");
            mcpTable["geospatialType"] = geospatialType;
        }

        table.AddRow(MessageService.GetString("command-settings-partition-key-label"), $"[white]{string.Join(',', view.PartitionKeyPaths)}[/]");
        table.HideHeaders();
        AnsiConsole.Write(table);

        // Full Text Policy section - only emitted when known
        if (view.FullTextPolicy is { } fullText)
        {
            AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-fulltext-title")}[/]");

            var fullTextTable = new Table();
            fullTextTable.AddColumns(string.Empty, string.Empty);

            var defaultLanguage = string.IsNullOrEmpty(fullText.DefaultLanguage)
                ? MessageService.GetString("command-settings-na")
                : fullText.DefaultLanguage;
            fullTextTable.AddRow(
                MessageService.GetString("command-settings-fulltext-default-language-label"),
                $"[white]{defaultLanguage}[/]");

            var mcpPaths = new List<Dictionary<string, object?>>();
            foreach (var path in fullText.Paths)
            {
                fullTextTable.AddRow(MessageService.GetString("command-settings-fulltext-path-label"), $"[white]{path.Path}[/]");
                fullTextTable.AddRow(MessageService.GetString("command-settings-fulltext-language-label"), $"[white]{path.Language}[/]");
                mcpPaths.Add(new Dictionary<string, object?>
                {
                    { "path", path.Path },
                    { "language", path.Language },
                });
            }

            mcpTable["fullTextPolicy"] = mcpPaths;
            fullTextTable.HideHeaders();
            AnsiConsole.Write(fullTextTable);
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(mcpTable));
        commandState.IsPrinted = true;
        return commandState;
    }

    private static bool TryGetPrincipialIdFromRbacException(Exception e, out string? principalId, out string? request, out string? permission)
    {
        var match = PrincipalIdRegex.Match(e.Message);
        if (match.Success)
        {
            request = match.Groups[1].Value;
            principalId = match.Groups[2].Value;
            permission = match.Groups[3].Value;
            return true;
        }

        request = null;
        principalId = null;
        permission = null;
        return false;
    }

    private static void AskForRBacPermissions(string principalId, string request, string permission)
    {
        AnsiConsole.Markup($"[red]{MessageService.GetString("error")}[/] ");
        ShellInterpreter.WriteLine(MessageService.GetArgsString("command-settings-rbac-error", "id", principalId, "request", request, "permission", permission));
    }

    private static async Task<CommandState> PrintOverviewAsync(CosmosClient client, CommandState commandState, CancellationToken token)
    {
        var acc = await client.ReadAccountAsync();

        AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-overview")}[/]");

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty, string.Empty, string.Empty);
        table.AddRow(MessageService.GetString("command-settings-account_id"), $"[white]{acc.Id}[/]", MessageService.GetString("command-settings-read_locations"), $"[white]{string.Join(", ", acc.ReadableRegions.Select(location => location.Name))}[/]");
        table.AddRow(MessageService.GetString("command-settings-uri"), $"[white]{client.Endpoint}[/]", MessageService.GetString("command-settings-write_locations"), $"[white]{string.Join(", ", acc.WritableRegions.Select(location => location.Name))}[/]");
        table.HideHeaders();
        AnsiConsole.Write(table);

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            accountId = acc.Id,
            uri = client.Endpoint,
            readLocations = acc.ReadableRegions.Select(location => location.Name).ToArray(),
            writeLocations = acc.WritableRegions.Select(location => location.Name).ToArray(),
        }));
        commandState.IsPrinted = true;
        return commandState;
    }
}
