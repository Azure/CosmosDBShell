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
            if (TryGetPrincipalIdFromRbacException(e, out var id, out var request, out var permission))
            {
                AskForRBacPermissions(id ?? string.Empty, request ?? string.Empty, permission ?? string.Empty);
                return commandState;
            }

            throw new CommandException("settings", e);
        }
    }

    private static async Task<CommandState> ShowContainerSettingsAsync(ConnectedState state, string databaseName, string containerName, CommandState commandState, CancellationToken token)
    {
        await ValidateContainerExistsAsync(state, databaseName, containerName, "settings", token);
        var view = await CosmosResourceFacade.GetContainerSettingsAsync(state, databaseName, containerName, token);
        var mcpTable = new Dictionary<string, object?>();

        // Scale section - fail gracefully if it cannot be read
        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-settings-scale-heading")));

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
                AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-settings-na")));
                break;
            default:
                if (TryGetPrincipalIdFromRbacMessage(view.ThroughputErrorMessage, out var rbacId, out var rbacRequest, out var rbacPermission))
                {
                    AskForRBacPermissions(rbacId ?? string.Empty, rbacRequest ?? string.Empty, rbacPermission ?? string.Empty);
                }
                else
                {
                    AnsiConsole.MarkupLine(Theme.FormatError(Markup.Escape(view.ThroughputErrorMessage ?? string.Empty)));
                }

                break;
        }

        AnsiConsole.MarkupLine(string.Empty);

        mcpTable["id"] = view.ContainerName;
        mcpTable["partitionKey"] = view.PartitionKeyPaths;
        mcpTable["analyticalTTL"] = view.AnalyticalStorageTtl;

        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-settings-title")));

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

        table.AddRow(MessageService.GetString("command-settings-ttl-label"), Theme.FormatTableValue(ttl));

        if (view.GeospatialType is { } geospatialType)
        {
            string geospatialLabel = string.Equals(geospatialType, "Geography", StringComparison.OrdinalIgnoreCase)
                ? MessageService.GetString("command-settings-geospatial-geography")
                : MessageService.GetString("command-settings-geospatial-geometry");
            table.AddRow(MessageService.GetString("command-settings-geospatial-label"), Theme.FormatTableValue(geospatialLabel));
            mcpTable["geospatialType"] = geospatialType;
        }

        table.AddRow(MessageService.GetString("command-settings-partition-key-label"), Theme.FormatTableValue(string.Join(',', view.PartitionKeyPaths)));
        table.HideHeaders();
        AnsiConsole.Write(table);

        // Full Text Policy section - show N/A when policy is unset
        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-settings-fulltext-title")));

        if (view.FullTextPolicy is { } fullText)
        {
            var fullTextTable = new Table();
            fullTextTable.AddColumns(string.Empty, string.Empty);

            var defaultLanguage = string.IsNullOrEmpty(fullText.DefaultLanguage)
                ? MessageService.GetString("command-settings-na")
                : fullText.DefaultLanguage;
            fullTextTable.AddRow(
                MessageService.GetString("command-settings-fulltext-default-language-label"),
                Theme.FormatTableValue(defaultLanguage));

            var mcpPaths = new List<Dictionary<string, object?>>();
            foreach (var path in fullText.Paths)
            {
                fullTextTable.AddRow(MessageService.GetString("command-settings-fulltext-path-label"), Theme.FormatTableValue(path.Path));
                fullTextTable.AddRow(MessageService.GetString("command-settings-fulltext-language-label"), Theme.FormatTableValue(path.Language ?? string.Empty));
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
        else
        {
            AnsiConsole.Markup("\t");
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-settings-na")));
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(mcpTable));
        commandState.IsPrinted = true;
        return commandState;
    }

    private static bool TryGetPrincipalIdFromRbacException(Exception e, out string? principalId, out string? request, out string? permission)
    {
        return TryGetPrincipalIdFromRbacMessage(e.Message, out principalId, out request, out permission);
    }

    private static bool TryGetPrincipalIdFromRbacMessage(string? message, out string? principalId, out string? request, out string? permission)
    {
        if (!string.IsNullOrEmpty(message))
        {
            var match = PrincipalIdRegex.Match(message);
            if (match.Success)
            {
                request = match.Groups[1].Value;
                principalId = match.Groups[2].Value;
                permission = match.Groups[3].Value;
                return true;
            }
        }

        request = null;
        principalId = null;
        permission = null;
        return false;
    }

    private static void AskForRBacPermissions(string principalId, string request, string permission)
    {
        AnsiConsole.Markup(Theme.FormatError(MessageService.GetString("error")) + " ");
        ShellInterpreter.WriteLine(MessageService.GetArgsString("command-settings-rbac-error", "id", principalId, "request", request, "permission", permission));
    }

    private static async Task<CommandState> PrintOverviewAsync(CosmosClient client, CommandState commandState, CancellationToken token)
    {
        var acc = await client.ReadAccountAsync();

        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-settings-overview")));

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty, string.Empty, string.Empty);
        table.AddRow(MessageService.GetString("command-settings-account_id"), Theme.FormatTableValue(acc.Id), MessageService.GetString("command-settings-read_locations"), Theme.FormatTableValue(string.Join(", ", acc.ReadableRegions.Select(location => location.Name))));
        table.AddRow(MessageService.GetString("command-settings-uri"), Theme.FormatTableValue(client.Endpoint.ToString()), MessageService.GetString("command-settings-write_locations"), Theme.FormatTableValue(string.Join(", ", acc.WritableRegions.Select(location => location.Name))));
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
