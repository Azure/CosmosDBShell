//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

[CosmosCommand("info")]
[CosmosExample("info", Description = "Display configuration and usage statistics for the current container, database, or account")]
[CosmosExample("info --database=MyDB --container=Products", Description = "Display info for a specific database and container")]
[CosmosExample("info --partitions", Description = "Add the per-physical-partition document distribution for a container")]
[CosmosExample("info --detailed", Description = "Add a storage breakdown and top partition keys (performs a full scan)")]
internal class InfoCommand : CosmosCommand
{
    private const string ResourceUsageHeader = "x-ms-resource-usage";
    private const int TopPartitionKeyLimit = 10;

    private static readonly Regex PrincipalIdRegex = new("Request for (.*) is blocked because principal \\[(.*)\\] does not have required RBAC permissions to perform action \\[(.*)\\]");

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("partitions", "p")]
    public bool Partitions { get; init; }

    [CosmosOption("detailed", "d")]
    public bool Detailed { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("info");
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
                return await this.ShowContainerSettingsAsync(connectedState, databaseName, containerName, commandState, token);
            }

            // If only a database is resolved, show database settings
            if (!string.IsNullOrEmpty(databaseName))
            {
                return await this.ShowDatabaseSettingsAsync(connectedState, databaseName, commandState, token);
            }

            // Otherwise show account overview
            return await this.PrintOverviewAsync(connectedState, commandState, token);
        }
        catch (Exception e)
        {
            if (TryGetPrincipalIdFromRbacException(e, out var id, out var request, out var permission))
            {
                AskForRBacPermissions(id ?? string.Empty, request ?? string.Empty, permission ?? string.Empty);
                return commandState;
            }

            throw new CommandException("info", e);
        }
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

    private static async Task WriteAccountDatabaseBreakdownAsync(ConnectedState state, IReadOnlyList<string> databaseNames, Dictionary<string, object?> mcpTable, CancellationToken token)
    {
        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-stats-account-databases-heading")));
        AnsiConsole.Markup("\t");
        AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-stats-account-detailed-cost-note")));

        long totalContainers = 0;
        long totalDocuments = 0;
        long totalSizeKb = 0;
        var rows = new List<(string Name, int ContainerCount, long DocumentCount, long SizeKb)>();
        var perDatabase = new List<Dictionary<string, object?>>(databaseNames.Count);

        foreach (var databaseName in databaseNames)
        {
            var database = state.Client.GetDatabase(databaseName);
            int containerCount = 0;
            long documents = 0;
            long sizeKb = 0;

            await foreach (var containerName in EnumerateContainerNamesAsync(state, databaseName, "info", token))
            {
                containerCount++;
                var usage = await ReadContainerUsageAsync(database.GetContainer(containerName), token);
                if (usage.DocumentCount is { } count)
                {
                    documents += count;
                }

                if (usage.TotalSizeKb is { } size)
                {
                    sizeKb += size;
                }
            }

            totalContainers += containerCount;
            totalDocuments += documents;
            totalSizeKb += sizeKb;
            rows.Add((databaseName, containerCount, documents, sizeKb));
            perDatabase.Add(new Dictionary<string, object?>
            {
                ["id"] = databaseName,
                ["containerCount"] = containerCount,
                ["documentCount"] = documents,
                ["totalSizeKb"] = sizeKb,
            });
        }

        mcpTable["totalContainers"] = totalContainers;
        mcpTable["documentCount"] = totalDocuments;
        mcpTable["totalSizeKb"] = totalSizeKb;
        mcpTable["databases"] = perDatabase;

        var totalsTable = new Table();
        totalsTable.AddColumns(string.Empty, string.Empty);
        totalsTable.HideHeaders();
        totalsTable.AddRow(MessageService.GetString("command-stats-account-label-total-containers"), Theme.FormatTableValue(totalContainers.ToString(CultureInfo.InvariantCulture)));
        totalsTable.AddRow(MessageService.GetString("command-stats-account-label-total-documents"), Theme.FormatTableValue(FormatCount(totalDocuments)));
        totalsTable.AddRow(MessageService.GetString("command-stats-account-label-total-size"), Theme.FormatTableValue(FormatSize(totalSizeKb)));
        AnsiConsole.Write(totalsTable);

        if (rows.Count == 0)
        {
            return;
        }

        var databaseTable = new Table();
        databaseTable.AddColumn(MessageService.GetString("command-stats-account-databases-col-name"));
        databaseTable.AddColumn(MessageService.GetString("command-stats-account-databases-col-containers"));
        databaseTable.AddColumn(MessageService.GetString("command-stats-account-databases-col-count"));
        databaseTable.AddColumn(MessageService.GetString("command-stats-account-databases-col-size"));
        foreach (var row in rows.OrderByDescending(r => r.SizeKb))
        {
            databaseTable.AddRow(
                Theme.FormatTableValue(row.Name),
                Theme.FormatTableValue(row.ContainerCount.ToString(CultureInfo.InvariantCulture)),
                Theme.FormatTableValue(FormatCount(row.DocumentCount)),
                Theme.FormatTableValue(FormatSize(row.SizeKb)));
        }

        AnsiConsole.Write(databaseTable);
    }

    private static async Task<ContainerUsageStats> ReadContainerUsageAsync(Container container, CancellationToken token)
    {
        var response = await container.ReadContainerAsync(new ContainerRequestOptions { PopulateQuotaInfo = true }, token);
        return ParseResourceUsage(response.Headers[ResourceUsageHeader]);
    }

    private static async Task WriteDatabaseThroughputAsync(Database database, Dictionary<string, object?> mcpTable, CancellationToken token)
    {
        int? min = null;
        int? max = null;
        try
        {
            var throughput = await database.ReadThroughputAsync(new RequestOptions(), token);
            min = throughput.MinThroughput;
            max = throughput.Resource?.AutoscaleMaxThroughput ?? throughput.Resource?.Throughput ?? min;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Database has no shared throughput; containers provide their own.
        }

        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-stats-throughput-heading")));

        if (min.HasValue || max.HasValue)
        {
            var table = new Table();
            table.AddColumns(string.Empty, string.Empty);
            table.HideHeaders();
            if (max is { } maxValue)
            {
                table.AddRow(MessageService.GetString("command-stats-throughput-max"), Theme.FormatTableValue(maxValue.ToString(CultureInfo.InvariantCulture)));
                mcpTable["maxThroughput"] = maxValue;
            }

            if (min is { } minValue)
            {
                table.AddRow(MessageService.GetString("command-stats-throughput-min"), Theme.FormatTableValue(minValue.ToString(CultureInfo.InvariantCulture)));
                mcpTable["minThroughput"] = minValue;
            }

            AnsiConsole.Write(table);
        }
        else
        {
            AnsiConsole.Markup("\t");
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-stats-database-shared-throughput-none")));
        }
    }

    private static async Task<List<Dictionary<string, object?>>> WritePartitionDistributionAsync(Container container, CancellationToken token)
    {
        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-stats-partitions-heading")));
        AnsiConsole.Markup("\t");
        AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-stats-partitions-cost-note")));

        var ranges = await container.GetFeedRangesAsync(token);
        var counts = new List<long>(ranges.Count);
        foreach (var range in ranges)
        {
            counts.Add(await CountFeedRangeAsync(container, range, token));
        }

        long total = counts.Sum();
        var result = new List<Dictionary<string, object?>>(counts.Count);

        var table = new Table();
        table.AddColumn(MessageService.GetString("command-stats-partitions-col-partition"));
        table.AddColumn(MessageService.GetString("command-stats-partitions-col-count"));
        table.AddColumn(MessageService.GetString("command-stats-partitions-col-share"));
        for (int i = 0; i < counts.Count; i++)
        {
            double share = total > 0 ? (double)counts[i] / total * 100 : 0;
            table.AddRow(
                Theme.FormatTableValue((i + 1).ToString(CultureInfo.InvariantCulture)),
                Theme.FormatTableValue(FormatCount(counts[i])),
                Theme.FormatTableValue(string.Create(CultureInfo.InvariantCulture, $"{share:0.#}%")));
            result.Add(new Dictionary<string, object?>
            {
                ["partition"] = i + 1,
                ["documentCount"] = counts[i],
                ["sharePercent"] = Math.Round(share, 1),
            });
        }

        AnsiConsole.Write(table);

        if (total > 0 && counts.Count > 1)
        {
            double largestShare = (double)counts.Max() / total * 100;
            AnsiConsole.Markup("\t");
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetArgsString(
                "command-stats-partitions-skew",
                "percent",
                string.Create(CultureInfo.InvariantCulture, $"{largestShare:0.#}"))));
        }

        return result;
    }

    private static async Task<List<Dictionary<string, object?>>> WriteTopPartitionKeysAsync(Container container, IReadOnlyList<string> partitionKeyPaths, CancellationToken token)
    {
        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-stats-detailed-heading")));
        AnsiConsole.Markup("\t");
        AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-stats-detailed-cost-note")));

        var result = new List<Dictionary<string, object?>>();
        if (partitionKeyPaths.Count == 0)
        {
            AnsiConsole.Markup("\t");
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-stats-na")));
            return result;
        }

        var projections = new List<string>(partitionKeyPaths.Count);
        for (int i = 0; i < partitionKeyPaths.Count; i++)
        {
            projections.Add(string.Create(CultureInfo.InvariantCulture, $"{BuildPartitionKeyPathExpression(partitionKeyPaths[i])} AS pk{i}"));
        }

        var groupBy = partitionKeyPaths.Select(BuildPartitionKeyPathExpression);
        var queryText = $"SELECT {string.Join(", ", projections)}, COUNT(1) AS count FROM c GROUP BY {string.Join(", ", groupBy)}";

        var groups = new List<(string Key, long Count)>();
        using var iterator = container.GetItemQueryIterator<JsonElement>(new QueryDefinition(queryText));
        while (iterator.HasMoreResults)
        {
            foreach (var element in await iterator.ReadNextAsync(token))
            {
                long count = element.TryGetProperty("count", out var countProperty) && countProperty.TryGetInt64(out var parsed) ? parsed : 0;
                var keyParts = new List<string>(partitionKeyPaths.Count);
                for (int i = 0; i < partitionKeyPaths.Count; i++)
                {
                    keyParts.Add(element.TryGetProperty($"pk{i}", out var keyProperty) ? DescribeKeyValue(keyProperty) : MessageService.GetString("command-stats-na"));
                }

                groups.Add((string.Join(" / ", keyParts), count));
            }
        }

        var table = new Table();
        table.AddColumn(MessageService.GetString("command-stats-detailed-col-key"));
        table.AddColumn(MessageService.GetString("command-stats-detailed-col-count"));
        foreach (var group in groups.OrderByDescending(g => g.Count).Take(TopPartitionKeyLimit))
        {
            table.AddRow(Theme.FormatTableValue(group.Key), Theme.FormatTableValue(FormatCount(group.Count)));
            result.Add(new Dictionary<string, object?>
            {
                ["partitionKey"] = group.Key,
                ["documentCount"] = group.Count,
            });
        }

        AnsiConsole.Write(table);
        return result;
    }

    private static async Task<long> CountFeedRangeAsync(Container container, FeedRange range, CancellationToken token)
    {
        long count = 0;
        using var iterator = container.GetItemQueryIterator<long>(
            range,
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c"));
        while (iterator.HasMoreResults)
        {
            foreach (var value in await iterator.ReadNextAsync(token))
            {
                count += value;
            }
        }

        return count;
    }

    private static string DescribeKeyValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => MessageService.GetString("command-stats-na"),
            JsonValueKind.Undefined => MessageService.GetString("command-stats-na"),
            _ => element.GetRawText(),
        };
    }

    private static string FormatCount(long? count)
    {
        return count?.ToString("N0", CultureInfo.InvariantCulture) ?? MessageService.GetString("command-stats-na");
    }

    /// <summary>
    /// Parses the Cosmos DB <c>x-ms-resource-usage</c> header into document count
    /// and storage sizes. The header is a semicolon-delimited list of key=value
    /// pairs, for example <c>documentsCount=42;documentsSize=128;collectionSize=160</c>.
    /// Sizes are expressed in kilobytes.
    /// </summary>
    internal static ContainerUsageStats ParseResourceUsage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return new ContainerUsageStats(null, null, null);
        }

        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = pair[..separator].Trim();
            var raw = pair[(separator + 1)..].Trim();
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                values[name] = parsed;
            }
        }

        long? documentCount = values.TryGetValue("documentsCount", out var count) ? count : null;
        long? dataSizeKb = values.TryGetValue("documentsSize", out var dataSize) ? dataSize : null;
        long? totalSizeKb = values.TryGetValue("collectionSize", out var totalSize) ? totalSize : null;

        return new ContainerUsageStats(documentCount, dataSizeKb, totalSizeKb);
    }

    /// <summary>
    /// Builds a NoSQL property accessor expression for a partition key path so it
    /// can be projected and grouped in a query. <c>/category</c> becomes
    /// <c>c["category"]</c> and <c>/a/b</c> becomes <c>c["a"]["b"]</c>.
    /// </summary>
    internal static string BuildPartitionKeyPathExpression(string path)
    {
        var builder = new StringBuilder("c");
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            builder.Append('[').Append('"').Append(segment.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"').Append(']');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats a kilobyte value into a human readable size string (KB, MB, GB, TB).
    /// </summary>
    internal static string FormatSize(long? kilobytes)
    {
        if (kilobytes is null)
        {
            return MessageService.GetString("command-stats-na");
        }

        double value = kilobytes.Value;
        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unit]}");
    }

    private async Task<CommandState> ShowContainerSettingsAsync(ConnectedState state, string databaseName, string containerName, CommandState commandState, CancellationToken token)
    {
        await ValidateContainerExistsAsync(state, databaseName, containerName, "info", token);
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
                    AnsiConsole.MarkupLine(Theme.FormatError(view.ThroughputErrorMessage ?? string.Empty));
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

        // Usage section - document count and storage size
        var container = state.Client.GetDatabase(databaseName).GetContainer(containerName);
        var usage = await ReadContainerUsageAsync(container, token);
        mcpTable["documentCount"] = usage.DocumentCount;
        mcpTable["dataSizeKb"] = usage.DataSizeKb;
        mcpTable["indexSizeKb"] = usage.IndexSizeKb;
        mcpTable["totalSizeKb"] = usage.TotalSizeKb;

        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-settings-usage-heading")));

        var usageTable = new Table();
        usageTable.AddColumns(string.Empty, string.Empty);
        usageTable.HideHeaders();
        usageTable.AddRow(MessageService.GetString("command-stats-label-document-count"), Theme.FormatTableValue(FormatCount(usage.DocumentCount)));
        usageTable.AddRow(MessageService.GetString("command-stats-label-data-size"), Theme.FormatTableValue(FormatSize(usage.DataSizeKb)));
        if (this.Detailed)
        {
            usageTable.AddRow(MessageService.GetString("command-stats-label-index-size"), Theme.FormatTableValue(FormatSize(usage.IndexSizeKb)));
        }

        usageTable.AddRow(MessageService.GetString("command-stats-label-total-size"), Theme.FormatTableValue(FormatSize(usage.TotalSizeKb)));
        AnsiConsole.Write(usageTable);

        if (this.Partitions)
        {
            mcpTable["partitions"] = await WritePartitionDistributionAsync(container, token);
        }

        if (this.Detailed)
        {
            mcpTable["topPartitionKeys"] = await WriteTopPartitionKeysAsync(container, view.PartitionKeyPaths, token);
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(mcpTable));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> ShowDatabaseSettingsAsync(ConnectedState state, string databaseName, CommandState commandState, CancellationToken token)
    {
        await ValidateDatabaseExistsAsync(state, databaseName, "info", token);

        var database = state.Client.GetDatabase(databaseName);

        long totalDocuments = 0;
        long totalSizeKb = 0;
        var perContainer = new List<Dictionary<string, object?>>();
        var rows = new List<(string Name, long? Count, long? SizeKb)>();

        await foreach (var name in EnumerateContainerNamesAsync(state, databaseName, "info", token))
        {
            var usage = await ReadContainerUsageAsync(database.GetContainer(name), token);
            if (usage.DocumentCount is { } count)
            {
                totalDocuments += count;
            }

            if (usage.TotalSizeKb is { } size)
            {
                totalSizeKb += size;
            }

            rows.Add((name, usage.DocumentCount, usage.TotalSizeKb));
            perContainer.Add(new Dictionary<string, object?>
            {
                ["id"] = name,
                ["documentCount"] = usage.DocumentCount,
                ["totalSizeKb"] = usage.TotalSizeKb,
            });
        }

        var mcpTable = new Dictionary<string, object?>
        {
            ["id"] = databaseName,
            ["containerCount"] = rows.Count,
            ["documentCount"] = totalDocuments,
            ["totalSizeKb"] = totalSizeKb,
        };

        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-stats-database-heading")));

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty);
        table.HideHeaders();
        table.AddRow(MessageService.GetString("command-stats-database-label-id"), Theme.FormatTableValue(databaseName));
        table.AddRow(MessageService.GetString("command-stats-database-label-container-count"), Theme.FormatTableValue(rows.Count.ToString(CultureInfo.InvariantCulture)));
        table.AddRow(MessageService.GetString("command-stats-database-label-total-documents"), Theme.FormatTableValue(FormatCount(totalDocuments)));
        table.AddRow(MessageService.GetString("command-stats-database-label-total-size"), Theme.FormatTableValue(FormatSize(totalSizeKb)));
        AnsiConsole.Write(table);

        await WriteDatabaseThroughputAsync(database, mcpTable, token);

        if (this.Detailed && rows.Count > 0)
        {
            AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-stats-containers-heading")));
            var containerTable = new Table();
            containerTable.AddColumn(MessageService.GetString("command-stats-containers-col-name"));
            containerTable.AddColumn(MessageService.GetString("command-stats-containers-col-count"));
            containerTable.AddColumn(MessageService.GetString("command-stats-containers-col-size"));
            foreach (var row in rows.OrderByDescending(r => r.SizeKb ?? 0))
            {
                containerTable.AddRow(
                    Theme.FormatTableValue(row.Name),
                    Theme.FormatTableValue(FormatCount(row.Count)),
                    Theme.FormatTableValue(FormatSize(row.SizeKb)));
            }

            AnsiConsole.Write(containerTable);
        }

        mcpTable["containers"] = perContainer;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(mcpTable));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> PrintOverviewAsync(ConnectedState state, CommandState commandState, CancellationToken token)
    {
        var client = state.Client;
        var acc = await client.ReadAccountAsync();

        var databaseNames = new List<string>();
        await foreach (var name in EnumerateDatabaseNamesAsync(state, "info", token))
        {
            databaseNames.Add(name);
        }

        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-settings-overview")));

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty, string.Empty, string.Empty);
        table.AddRow(MessageService.GetString("command-settings-account_id"), Theme.FormatTableValue(acc.Id), MessageService.GetString("command-settings-read_locations"), Theme.FormatTableValue(string.Join(", ", acc.ReadableRegions.Select(location => location.Name))));
        table.AddRow(MessageService.GetString("command-settings-uri"), Theme.FormatTableValue(client.Endpoint.ToString()), MessageService.GetString("command-settings-write_locations"), Theme.FormatTableValue(string.Join(", ", acc.WritableRegions.Select(location => location.Name))));
        table.AddRow(MessageService.GetString("command-stats-account-label-database-count"), Theme.FormatTableValue(databaseNames.Count.ToString(CultureInfo.InvariantCulture)), string.Empty, string.Empty);
        table.HideHeaders();
        AnsiConsole.Write(table);

        var mcpTable = new Dictionary<string, object?>
        {
            ["accountId"] = acc.Id,
            ["uri"] = client.Endpoint.ToString(),
            ["readLocations"] = acc.ReadableRegions.Select(location => location.Name).ToArray(),
            ["writeLocations"] = acc.WritableRegions.Select(location => location.Name).ToArray(),
            ["databaseCount"] = databaseNames.Count,
        };

        if (this.Detailed)
        {
            await WriteAccountDatabaseBreakdownAsync(state, databaseNames, mcpTable, token);
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(mcpTable));
        commandState.IsPrinted = true;
        return commandState;
    }

    internal sealed record ContainerUsageStats(long? DocumentCount, long? DataSizeKb, long? TotalSizeKb)
    {
        public long? IndexSizeKb => this.TotalSizeKb is { } total && this.DataSizeKb is { } data
            ? Math.Max(0, total - data)
            : null;
    }
}
