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
    /// <summary>
    /// Cosmos DB substatus returned when no dedicated throughput offer exists for the current container.
    /// </summary>
    private const int ThroughputNotConfiguredSubStatusCode = 1003;

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
                return await ShowContainerSettingsAsync(connectedState.Client, databaseName, containerName, commandState, token);
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

    private static async Task<CommandState> ShowContainerSettingsAsync(CosmosClient client, string databaseName, string containerName, CommandState commandState, CancellationToken token)
    {
        var database = client.GetDatabase(databaseName);
        var container = database.GetContainer(containerName);
        var mcpTable = new Dictionary<string, object?>();

        // Scale section - fail gracefully if it cannot be read
        AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-scale-heading")}[/]");

        try
        {
            var throughputResponse = await container.ReadThroughputAsync(null, cancellationToken: token);
            var min = throughputResponse.MinThroughput ?? 0;
            var max = throughputResponse.Resource.AutoscaleMaxThroughput.HasValue ? throughputResponse.Resource.AutoscaleMaxThroughput.Value : throughputResponse.Resource.Throughput;
            AnsiConsole.Markup("\t");
            AnsiConsole.MarkupLine(MessageService.GetArgsString("command-settings-scale-usage", "min", min, "max", max ?? min));
            mcpTable["minThroughput"] = min;
            mcpTable["maxThroughput"] = max ?? min;
        }
        catch (Exception e)
        {
            AnsiConsole.Markup("\t");
            if (TryGetPrincipialIdFromRbacException(e, out var id, out var request, out var permission))
            {
                AskForRBacPermissions(id ?? string.Empty, request ?? string.Empty, permission ?? string.Empty);
            }
            else if (IsThroughputNotConfiguredException(e))
            {
                // No dedicated throughput is configured on this container - show N/A
                AnsiConsole.MarkupLine($"[{Theme.MutedColorName}]{MessageService.GetString("command-settings-na")}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[{Theme.ErrorColorName}]{Markup.Escape(CommandException.GetDisplayMessage(e))}[/]");
            }
        }

        AnsiConsole.MarkupLine(string.Empty);

        // Container settings section - fail gracefully if it cannot be read
        ContainerProperties? resource = null;
        try
        {
            var containerResponse = await container.ReadContainerAsync(cancellationToken: token);
            resource = containerResponse.Resource;
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-title")}[/]");
            AnsiConsole.Markup("\t");
            if (TryGetPrincipialIdFromRbacException(e, out var id, out var request, out var permission))
            {
                AskForRBacPermissions(id ?? string.Empty, request ?? string.Empty, permission ?? string.Empty);
            }
            else
            {
                AnsiConsole.MarkupLine($"[{Theme.ErrorColorName}]{Markup.Escape(CommandException.GetDisplayMessage(e))}[/]");
            }

            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(mcpTable));
            commandState.IsPrinted = true;
            return commandState;
        }

        if (resource == null)
        {
            AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-title")}[/]");
            AnsiConsole.Markup("\t");
            AnsiConsole.MarkupLine($"[{Theme.WarningColorName}]{MessageService.GetString("command-settings-not-available")}[/]");
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(mcpTable));
            commandState.IsPrinted = true;
            return commandState;
        }

        mcpTable["id"] = resource.Id;
        mcpTable["partitionKey"] = resource.PartitionKeyPaths;
        mcpTable["analyticalTTL"] = resource.AnalyticalStoreTimeToLiveInSeconds;

        AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-title")}[/]");

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty);

        string ttl;
        if (resource.AnalyticalStoreTimeToLiveInSeconds == null ||
            resource.AnalyticalStoreTimeToLiveInSeconds == 0)
        {
            ttl = MessageService.GetString("command-settings-Off");
        }
        else if (resource.AnalyticalStoreTimeToLiveInSeconds == -1)
        {
            ttl = MessageService.GetString("command-settings-On");
        }
        else
        {
            ttl = MessageService.GetArgsString(
                  "command-settings-ttl-seconds",
                  "seconds",
                  resource.AnalyticalStoreTimeToLiveInSeconds);
        }

        table.AddRow(MessageService.GetString("command-settings-ttl-label"), $"[{Theme.TableValueColorName}]{ttl}[/]");

        string label;
        switch (resource.GeospatialConfig.GeospatialType)
        {
            case GeospatialType.Geography:
                label = MessageService.GetString("command-settings-geospatial-geography");
                mcpTable["geospatialType"] = "Geography";
                break;
            default:
                mcpTable["geospatialType"] = "Geometry";
                label = MessageService.GetString("command-settings-geospatial-geometry");
                break;
        }

        table.AddRow(MessageService.GetString("command-settings-geospatial-label"), $"[{Theme.TableValueColorName}]{label}[/]");
        table.AddRow(MessageService.GetString("command-settings-partition-key-label"), $"[{Theme.TableValueColorName}]{string.Join(',', resource.PartitionKeyPaths)}[/]");
        table.HideHeaders();
        AnsiConsole.Write(table);

        // Full Text Policy section - show N/A if unset
        AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-fulltext-title")}[/]");

        if (resource.FullTextPolicy != null)
        {
            table = new Table();
            table.AddColumns(string.Empty, string.Empty);

            string defaultLanguage = resource.FullTextPolicy.DefaultLanguage;
            table.AddRow(
                MessageService.GetString("command-settings-fulltext-default-language-label"),
                $"[{Theme.TableValueColorName}]{(string.IsNullOrEmpty(defaultLanguage) ? MessageService.GetString("command-settings-na") : defaultLanguage)}[/]");

            var fullTextPaths = new List<Dictionary<string, object?>>();

            foreach (var path in resource.FullTextPolicy.FullTextPaths)
            {
                table.AddRow(MessageService.GetString("command-settings-fulltext-path-label"), $"[{Theme.TableValueColorName}]{path.Path}[/]");
                table.AddRow(MessageService.GetString("command-settings-fulltext-language-label"), $"[{Theme.TableValueColorName}]{path.Language}[/]");

                fullTextPaths.Add(new Dictionary<string, object?>
                {
                    { "path", path.Path },
                    { "language", path.Language },
                });
            }

            mcpTable["fullTextPolicy"] = fullTextPaths;
            table.HideHeaders();
            AnsiConsole.Write(table);
        }
        else
        {
            AnsiConsole.Markup("\t");
            AnsiConsole.MarkupLine($"[{Theme.MutedColorName}]{MessageService.GetString("command-settings-na")}[/]");
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
        AnsiConsole.Markup($"[{Theme.ErrorColorName}]{MessageService.GetString("error")}[/] ");
        ShellInterpreter.WriteLine(MessageService.GetArgsString("command-settings-rbac-error", "id", principalId, "request", request, "permission", permission));
    }

    /// <summary>
    /// Checks if the exception indicates that no dedicated throughput is configured on the container
    /// (e.g., Serverless accounts, Emulators, or containers using shared database throughput).
    /// </summary>
    private static bool IsThroughputNotConfiguredException(Exception e)
    {
        if (e is not CosmosException cosmosEx ||
            cosmosEx.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        if ((int)cosmosEx.SubStatusCode == ThroughputNotConfiguredSubStatusCode)
        {
            // Cosmos DB uses this substatus when the container has no dedicated throughput offer.
            return true;
        }

        return cosmosEx.Message.Contains("Throughput is not configured", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CommandState> PrintOverviewAsync(CosmosClient client, CommandState commandState, CancellationToken token)
    {
        var acc = await client.ReadAccountAsync();

        AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-settings-overview")}[/]");

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty, string.Empty, string.Empty);
        table.AddRow(MessageService.GetString("command-settings-account_id"), $"[{Theme.TableValueColorName}]{acc.Id}[/]", MessageService.GetString("command-settings-read_locations"), $"[{Theme.TableValueColorName}]{string.Join(", ", acc.ReadableRegions.Select(location => location.Name))}[/]");
        table.AddRow(MessageService.GetString("command-settings-uri"), $"[{Theme.TableValueColorName}]{client.Endpoint}[/]", MessageService.GetString("command-settings-write_locations"), $"[{Theme.TableValueColorName}]{string.Join(", ", acc.WritableRegions.Select(location => location.Name))}[/]");
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
