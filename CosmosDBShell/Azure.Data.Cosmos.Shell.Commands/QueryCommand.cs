//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

internal enum MetricTarget
{
    Display,
    File,
}

[CosmosCommand("query")]
[CosmosExample("query \"SELECT * FROM c\"", Description = "Query all documents from container")]
[CosmosExample("query \"SELECT * FROM c WHERE c.status = 'active'\"", Description = "Query with filter condition")]
[CosmosExample("query \"SELECT c.id, c.name FROM c\" -max=10", Description = "Query specific fields with result limit")]
[CosmosExample("query \"SELECT * FROM c\" -max=0", Description = "Query all matching documents without a limit")]
[CosmosExample("query \"SELECT * FROM c\" -metrics=Display", Description = "Query with performance metrics displayed")]
[CosmosExample("query \"SELECT * FROM c WHERE c.city = 'Seattle'\" --explain", Description = "Show the query execution plan and index usage without returning documents")]
[CosmosExample("query \"SELECT * FROM c\" --database=MyDB --container=Products", Description = "Query specific database and container")]
[McpAnnotation(
    Title = "Run Query",
    ReadOnly = true,
    Idempotent = true,
    OpenWorld = true,
    Description = "Executes a Cosmos DB NoSQL query against the current container and returns matching documents. Pass explain=true to return the query execution plan (utilized/potential indexes and a plain-language evaluation) instead of documents. Use the cosmos://docs/nosql-query-language resource for query syntax reference.")]
internal class QueryCommand : CosmosCommand
{
    [CosmosParameter("query")]
    public string? Query { get; init; }

    [CosmosOption("max", "m")]
    public int? Max { get; init; }

    [CosmosOption("metrics", "mx", DefaultValue = MetricTarget.Display)]
    public MetricTarget? Metrics { get; init; }

    [CosmosOption("format", "f")]
    public string? OutputFormat { get; init; }

    [CosmosOption("bucket")]
    public int? Bucket { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("explain")]
    public bool? Explain { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (this.Bucket.HasValue && !BucketCommand.CheckBucket(this.Bucket.Value))
        {
            return new CommandState();
        }

        // Get connected state
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("query");
        }

        // Resolve container using the helper
        var (databaseName, containerName, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "query",
            token);

        if (this.Explain == true)
        {
            return await this.ExecuteExplainAsync(container, shell, token);
        }

        return await this.ExecuteQueryAsync(container, shell, token);
    }

    internal static List<JsonElement> CollectDocuments(IEnumerable<JsonElement> currentDocuments, JsonElement pageDocuments, int? maxItemCount)
    {
        var documents = currentDocuments.ToList();

        // Clone the entire page array once so the caller can safely dispose the
        // per-page JsonDocument that owns pageDocuments. Per-element Clone()
        // would allocate a fresh JsonDocument for every result, which is very
        // expensive for large pages; cloning the page produces a single backing
        // document whose elements we can reference directly.
        var clonedPage = pageDocuments.Clone();
        foreach (var resultDocument in clonedPage.EnumerateArray())
        {
            if (maxItemCount >= 0 && documents.Count >= maxItemCount)
            {
                break;
            }

            documents.Add(resultDocument);
        }

        return documents;
    }

    internal static bool PageExceedsLimit(int currentCount, JsonElement pageDocuments, int? maxItemCount)
    {
        if (!maxItemCount.HasValue)
        {
            return false;
        }

        var remainingCapacity = maxItemCount.Value - currentCount;
        if (remainingCapacity <= 0)
        {
            return pageDocuments.GetArrayLength() > 0;
        }

        return pageDocuments.GetArrayLength() > remainingCapacity;
    }

    internal static List<Dictionary<string, object>> GetMetrics(ResponseMessage msg)
    {
        var queryMetrics = msg.Diagnostics.GetQueryMetrics();
        return BuildMetrics(queryMetrics?.TotalRequestCharge ?? 0, queryMetrics?.CumulativeMetrics);
    }

    internal static List<Dictionary<string, object>> BuildMetrics(double totalRequestCharge, ServerSideMetrics? cumulative)
    {
        return [
            new Dictionary<string, object>
            {
                { "metric", "Request Charge" },
                { "value",  totalRequestCharge },
                { "formattedValue", $"{totalRequestCharge} RUs" },
                { "tooltip", "Request Charge" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Retrieved document count" },
                { "value",  (object?)cumulative?.RetrievedDocumentCount ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.RetrievedDocumentCount}" : "N/A" },
                { "tooltip", "Total number of retrieved documents" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Retrieved document size" },
                { "value",  (object?)cumulative?.RetrievedDocumentSize ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.RetrievedDocumentSize} bytes" : "N/A" },
                { "tooltip", "Total size of retrieved documents in bytes" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Output document count" },
                { "value",  (object?)cumulative?.OutputDocumentCount ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.OutputDocumentCount}" : "N/A" },
                { "tooltip", "Total Number of output documents" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Output document size" },
                { "value",  (object?)cumulative?.OutputDocumentSize ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.OutputDocumentSize} bytes" : "N/A" },
                { "tooltip", "Total size of output documents in bytes" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Index hit ratio" },
                { "value",  (object?)cumulative?.IndexHitRatio ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.IndexHitRatio}" : "N/A" },
                { "tooltip", "The index hit ratio by query in the Azure Cosmos database service. Value is within the range [0,1]." },
            },

            new Dictionary<string, object>
            {
                { "metric", "Index lookup time" },
                { "value",  (object?)cumulative?.IndexLookupTime.TotalMilliseconds ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.IndexLookupTime.TotalMilliseconds} ms" : "N/A" },
                { "tooltip", "The query index lookup time in the Azure Cosmos database service." },
            },

            new Dictionary<string, object>
            {
                { "metric", "Document load time" },
                { "value",  (object?)cumulative?.DocumentLoadTime.TotalMilliseconds ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.DocumentLoadTime.TotalMilliseconds} ms" : "N/A" },
                { "tooltip", "Time spent in loading documents" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Runtime execution time" },
                { "value",  (object?)cumulative?.RuntimeExecutionTime.TotalMilliseconds ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.RuntimeExecutionTime.TotalMilliseconds} ms" : "N/A" },
                { "tooltip", "The query runtime execution time in the Azure Cosmos database service." },
            },

            new Dictionary<string, object>
            {
                { "metric", "VMExecution execution time" },
                { "value",  (object?)cumulative?.VMExecutionTime.TotalMilliseconds ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.VMExecutionTime.TotalMilliseconds} ms" : "N/A" },
                { "tooltip", "Total time spent executing the virtual machine" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Query preparation time" },
                { "value",  (object?)cumulative?.QueryPreparationTime.TotalMilliseconds ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.QueryPreparationTime.TotalMilliseconds} ms" : "N/A" },
                { "tooltip", "The query preparation time in the Azure Cosmos database service." },
            },

            new Dictionary<string, object>
            {
                { "metric", "Document write time" },
                { "value",  (object?)cumulative?.DocumentWriteTime.TotalMilliseconds ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.DocumentWriteTime.TotalMilliseconds} ms" : "N/A" },
                { "tooltip", "Time spent to write query result set to response buffer" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Total time" },
                { "value",  (object?)cumulative?.TotalTime.TotalMilliseconds ?? null! },
                { "formattedValue", cumulative != null ? $"{cumulative.TotalTime.TotalMilliseconds} ms" : "N/A" },
                { "tooltip", "The total query time in the Azure Cosmos database service." },
            },
        ];
    }

    private static void AddIndexTable(Table table, string title, JsonElement? jToken)
    {
        if (jToken == null || jToken.Value.ValueKind == JsonValueKind.Null || jToken.Value.ValueKind == JsonValueKind.Undefined)
        {
            table.AddRow(Theme.FormatTableValue(title), Theme.FormatTableValue("-"), Theme.FormatTableValue("-"));
            return;
        }

        if (jToken.Value.ValueKind == JsonValueKind.Array)
        {
            var arr = jToken.Value.EnumerateArray().ToList();
            if (arr.Count == 0)
            {
                table.AddRow(Theme.FormatTableValue(title), Theme.FormatTableValue("-"), Theme.FormatTableValue("-"));
                return;
            }

            var i = 0;
            while (i < arr.Count)
            {
                string col1 = arr[i].ToString();
                i += 1;
                string col2;
                if (i < arr.Count)
                {
                    col2 = arr[i].ToString();
                }
                else
                {
                    col2 = "-";
                }

                i += 1;
                table.AddRow(Theme.FormatTableValue(title), Theme.FormatTableValue(col1), Theme.FormatTableValue(col2));
            }
        }
    }

    private static void GeneratePlainResultDocument(CommandState returnState, IEnumerable<JsonElement> documents)
    {
        returnState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { items = documents.ToList() }));
    }

    // Parses the raw IndexMetrics JSON returned by Cosmos (PopulateIndexMetrics = true)
    // into flat lists of utilized and potential index specifications. The metrics group
    // single and composite indexes separately; both are flattened here because the
    // evaluation only cares about whether an index contributed, not its arity.
    internal static (List<string> Utilized, List<string> Potential) ParseIndexPlan(string? indexMetricsJson)
    {
        var utilized = new List<string>();
        var potential = new List<string>();

        if (string.IsNullOrWhiteSpace(indexMetricsJson))
        {
            return (utilized, potential);
        }

        try
        {
            using var doc = JsonDocument.Parse(indexMetricsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (utilized, potential);
            }

            if (root.TryGetProperty("UtilizedIndexes", out var utilizedGroup))
            {
                AddIndexSpecs(utilizedGroup, utilized);
            }

            if (root.TryGetProperty("PotentialIndexes", out var potentialGroup))
            {
                AddIndexSpecs(potentialGroup, potential);
            }
        }
        catch (JsonException)
        {
            // The index metrics payload was not the expected JSON shape; treat as
            // "no plan details available" rather than failing the explain.
        }

        return (utilized, potential);
    }

    // Builds a structured evaluation of an index plan. Pure and side-effect free so it
    // can be unit tested without a live Cosmos response. A query is reported as a full
    // scan when no index contributed; otherwise it is an index seek.
    internal static PlanEvaluation EvaluatePlan(
        IReadOnlyList<string> utilizedIndexes,
        IReadOnlyList<string> potentialIndexes,
        double? indexHitRatio,
        long? retrievedDocumentCount,
        long? outputDocumentCount)
    {
        bool indexSeek = utilizedIndexes.Count > 0;
        bool fullScan = !indexSeek;
        return new PlanEvaluation(
            fullScan,
            indexSeek,
            indexHitRatio,
            retrievedDocumentCount,
            outputDocumentCount,
            utilizedIndexes,
            potentialIndexes);
    }

    private static void AddIndexSpecs(JsonElement group, List<string> target)
    {
        if (group.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var kind in new[] { "SingleIndexes", "CompositeIndexes" })
        {
            if (group.TryGetProperty(kind, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    var spec = ExtractIndexSpec(element);
                    if (!string.IsNullOrEmpty(spec))
                    {
                        target.Add(spec);
                    }
                }
            }
        }
    }

    private static string? ExtractIndexSpec(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("IndexSpec", out var spec) && spec.ValueKind == JsonValueKind.String)
            {
                return spec.GetString();
            }

            if (element.TryGetProperty("IndexSpecs", out var specs) && specs.ValueKind == JsonValueKind.Array)
            {
                var paths = new List<string>();
                foreach (var path in specs.EnumerateArray())
                {
                    if (path.ValueKind == JsonValueKind.String)
                    {
                        var value = path.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            paths.Add(value);
                        }
                    }
                }

                return string.Join(", ", paths);
            }
        }

        return element.ToString();
    }

    private static List<string> BuildPlanMessages(PlanEvaluation evaluation)
    {
        var messages = new List<string>();

        if (evaluation.FullScan)
        {
            messages.Add(MessageService.GetString("command-query-explain-full_scan"));
        }
        else
        {
            messages.Add(MessageService.GetArgsString(
                "command-query-explain-index_seek",
                "indexes",
                string.Join(", ", evaluation.UtilizedIndexes)));
        }

        if (evaluation.PotentialIndexes.Count > 0)
        {
            messages.Add(MessageService.GetArgsString(
                "command-query-explain-recommend_index",
                "indexes",
                string.Join(", ", evaluation.PotentialIndexes)));
        }

        if (evaluation.IndexHitRatio.HasValue)
        {
            messages.Add(MessageService.GetArgsString(
                "command-query-explain-hit_ratio",
                "ratio",
                evaluation.IndexHitRatio.Value));
        }

        return messages;
    }

    private static ShellJson BuildExplainJson(string? query, PlanEvaluation evaluation, double requestCharge, IReadOnlyList<string> messages)
    {
        var element = JsonSerializer.SerializeToElement(new
        {
            query,
            estimate = true,
            plan = new
            {
                utilizedIndexes = evaluation.UtilizedIndexes,
                potentialIndexes = evaluation.PotentialIndexes,
                indexHitRatio = evaluation.IndexHitRatio,
                retrievedDocumentCount = evaluation.RetrievedDocumentCount,
                outputDocumentCount = evaluation.OutputDocumentCount,
                requestCharge,
            },
            evaluation = new
            {
                fullScan = evaluation.FullScan,
                indexSeek = evaluation.IndexSeek,
                messages,
            },
        });

        return new ShellJson(element);
    }

    private static void RenderExplain(PlanEvaluation evaluation, double requestCharge, IReadOnlyList<string> messages)
    {
        AnsiConsole.MarkupLine(MessageService.GetString("command-query-explain-header"));

        foreach (var message in messages)
        {
            AnsiConsole.MarkupLine(Markup.Escape(message));
        }

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty);
        table.HideHeaders();
        table.AddRow(
            Theme.FormatHelpName(Markup.Escape(MessageService.GetString("command-query-explain-utilized"))),
            Theme.FormatTableValue(Markup.Escape(evaluation.UtilizedIndexes.Count > 0 ? string.Join(", ", evaluation.UtilizedIndexes) : "-")));
        table.AddRow(
            Theme.FormatHelpName(Markup.Escape(MessageService.GetString("command-query-explain-potential"))),
            Theme.FormatTableValue(Markup.Escape(evaluation.PotentialIndexes.Count > 0 ? string.Join(", ", evaluation.PotentialIndexes) : "-")));
        table.AddRow(
            Theme.FormatHelpName(Markup.Escape(MessageService.GetString("command-query-index_hit_ratio"))),
            Theme.FormatTableValue(Markup.Escape(evaluation.IndexHitRatio?.ToString(CultureInfo.InvariantCulture) ?? "N/A")));
        table.AddRow(
            Theme.FormatHelpName(Markup.Escape(MessageService.GetString("command-query-retrieved"))),
            Theme.FormatTableValue(Markup.Escape(evaluation.RetrievedDocumentCount?.ToString(CultureInfo.InvariantCulture) ?? "N/A")));
        table.AddRow(
            Theme.FormatHelpName(Markup.Escape(MessageService.GetString("command-query-output"))),
            Theme.FormatTableValue(Markup.Escape(evaluation.OutputDocumentCount?.ToString(CultureInfo.InvariantCulture) ?? "N/A")));
        table.AddRow(
            Theme.FormatHelpName(Markup.Escape(MessageService.GetString("command-query-explain-charge"))),
            Theme.FormatTableValue(Markup.Escape(requestCharge.ToString(CultureInfo.InvariantCulture))));
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine(MessageService.GetString("command-query-explain-estimate_note"));
    }

    private async Task ThrowIfRequestFailedAsync(ResponseMessage response, ShellInterpreter shell)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorContent = string.Empty;
        if (response.Content != null)
        {
            using var errorStreamReader = new StreamReader(response.Content);
            errorContent = await errorStreamReader.ReadToEndAsync();
        }

        var message = string.IsNullOrWhiteSpace(response.ErrorMessage) ? errorContent : response.ErrorMessage;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = MessageService.GetString("command-query-error-request_failed", new Dictionary<string, object>
            {
                { "statusCode", (int)response.StatusCode },
                { "status", response.StatusCode },
            });
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest
            && shell.TryReportQueryError(this.Query ?? string.Empty, message))
        {
            // The shell has already emitted a compiler-style diagnostic with
            // line/column/caret; throw a marker exception so ReportExecutionError
            // stays silent.
            throw new CommandReportedException("query", new InvalidOperationException(message));
        }

        throw CommandException.FromResponseStatus("query", response.StatusCode, message);
    }

    private async Task<CommandState> ExecuteExplainAsync(Container container, ShellInterpreter shell, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.Query))
        {
            throw new CommandException("query", MessageService.GetString("command-query-error-empty_query"));
        }

        var returnState = new CommandState();
        returnState.SetFormat(this.OutputFormat ?? Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT"));

        try
        {
            // The query must execute to obtain index metrics; Cosmos has no zero-cost
            // EXPLAIN. Reading only the first page keeps the RU cost low while still
            // reflecting the plan and index usage chosen by the query engine.
            var options = new QueryRequestOptions
            {
                PopulateIndexMetrics = true,
                MaxItemCount = 1,
            };

            if (this.Bucket.HasValue)
            {
                options.ThroughputBucket = this.Bucket.Value;
            }

            using var feedIterator = container.GetItemQueryStreamIterator(this.Query, null, options);

            ResponseMessage? response = null;
            if (feedIterator.HasMoreResults)
            {
                response = await feedIterator.ReadNextAsync(token);
                await this.ThrowIfRequestFailedAsync(response, shell);
            }

            var cumulative = response?.Diagnostics.GetQueryMetrics()?.CumulativeMetrics;
            double requestCharge = response?.Diagnostics.GetQueryMetrics()?.TotalRequestCharge ?? 0;

            var (utilized, potential) = ParseIndexPlan(response?.IndexMetrics);
            var evaluation = EvaluatePlan(
                utilized,
                potential,
                cumulative?.IndexHitRatio,
                cumulative?.RetrievedDocumentCount,
                cumulative?.OutputDocumentCount);
            var messages = BuildPlanMessages(evaluation);

            // Emit JSON only for machine consumers (MCP, output redirection) or when
            // the user explicitly asked for JSON. Interactive sessions get the
            // human-readable table even though JSon is the default enum value.
            var explicitJson = string.Equals(this.OutputFormat, "json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(this.OutputFormat, "js", StringComparison.OrdinalIgnoreCase);

            if (shell.McpPort.HasValue || shell.StdOutRedirect != null || explicitJson)
            {
                returnState.Result = BuildExplainJson(this.Query, evaluation, requestCharge, messages);
                return returnState;
            }

            RenderExplain(evaluation, requestCharge, messages);
            returnState.IsPrinted = true;
            return returnState;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException e)
        {
            throw new CommandException("query", e);
        }
        catch (CommandReportedException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new CommandException("query", e);
        }
    }

    private async Task<CommandState> ExecuteQueryAsync(Container container, ShellInterpreter shell, CancellationToken token)
    {
        var returnState = new CommandState();
        returnState.SetFormat(this.OutputFormat ?? Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT"));
        var aggregatedDocuments = new List<JsonElement>();

        try
        {
            var options = new QueryRequestOptions
            {
                PopulateIndexMetrics = true,
            };

            var effectiveMaxItemCount = ResultLimit.ResolveMaxItemCount(this.Max, defaultMaxItemCount: null);
            if (effectiveMaxItemCount.HasValue)
            {
                options.MaxItemCount = effectiveMaxItemCount.Value;
            }

            if (this.Bucket.HasValue)
            {
                options.ThroughputBucket = this.Bucket.Value;
            }

            if (string.IsNullOrWhiteSpace(this.Query))
            {
                throw new CommandException("query", MessageService.GetString("command-query-error-empty_query"));
            }

            using var feedIterator = container.GetItemQueryStreamIterator(this.Query, null, options);
            var limitReached = false;

            while (feedIterator.HasMoreResults)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var response = await feedIterator.ReadNextAsync(token);

                await this.ThrowIfRequestFailedAsync(response, shell);

                if (response.Content == null)
                {
                    throw new CommandException("query", MessageService.GetString("command-query-error-no_content_stream"));
                }

                using var streamReader = new StreamReader(response.Content);
                var responseContent = await streamReader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(responseContent))
                {
                    throw new CommandException("query", MessageService.GetString("command-query-error-empty_content"));
                }

                using var queryDocument = JsonDocument.Parse(responseContent);
                ShellInterpreter.WriteLine(MessageService.GetString("command-query-fetched", new Dictionary<string, object> { { "count", queryDocument.RootElement.GetProperty("_count").ToString() } }));
                var queryMetrics = response.Diagnostics.GetQueryMetrics();
                if (queryMetrics != null)
                {
                    AnsiConsole.MarkupLine(MessageService.GetString("command-query-request_charge", new Dictionary<string, object> { { "charge", queryMetrics.TotalRequestCharge.ToString() } }));
                }

                var pageDocuments = queryDocument.RootElement.GetProperty("Documents");
                var pageExceedsLimit = PageExceedsLimit(aggregatedDocuments.Count, pageDocuments, effectiveMaxItemCount);
                aggregatedDocuments = CollectDocuments(aggregatedDocuments, pageDocuments, effectiveMaxItemCount);

                if (this.Metrics == MetricTarget.File)
                {
                    var metricProperty = GetMetrics(response);
                    var parsedIndexMetrics = response.IndexMetrics != null
                        ? Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(response.IndexMetrics)
                        : null;

                    if (shell.StdOutRedirect == null || !string.Equals("csv", this.OutputFormat, StringComparison.OrdinalIgnoreCase))
                    {
                        var element = System.Text.Json.JsonSerializer.SerializeToElement(
                            new Dictionary<string, object>()
                            {
                                { "documents", aggregatedDocuments },
                                { "requestCharge", queryMetrics?.TotalRequestCharge ?? 0 },
                                { "queryMetrics", metricProperty },
                                { "indexMetrics", parsedIndexMetrics ?? new Dictionary<string, object>() },
                            });
                        returnState.Result = new ShellJson(element);
                    }
                    else
                    {
                        GeneratePlainResultDocument(returnState, aggregatedDocuments);
                        var sb = new StringBuilder();
                        var first = true;
                        var sep = ShellInterpreter.CSVSeparator;
                        foreach (var dict in metricProperty)
                        {
                            if (!first)
                            {
                                sb.Append(sep);
                            }
                            else
                            {
                                first = false;
                            }

                            sb.Append(CommandState.EscapeCSV(dict["metric"]));
                        }

                        sb.AppendLine();

                        first = true;
                        foreach (var dict in metricProperty)
                        {
                            if (!first)
                            {
                                sb.Append(sep);
                            }
                            else
                            {
                                first = false;
                            }

                            sb.Append(CommandState.EscapeCSV(dict["value"]));
                        }

                        sb.AppendLine();

                        var outFile = Path.ChangeExtension(shell.StdOutRedirect, "metrics.csv");
                        if (outFile == shell.StdOutRedirect)
                        {
                            outFile = Path.ChangeExtension(shell.StdOutRedirect, "metrics2.csv");
                        }

                        if (shell.AppendOutRedirection)
                        {
                            File.AppendAllText(outFile, sb.ToString());
                        }
                        else
                        {
                            File.WriteAllText(outFile, sb.ToString());
                        }
                    }
                }
                else if (this.Metrics == MetricTarget.Display)
                {
                    GeneratePlainResultDocument(returnState, aggregatedDocuments);

                    var displayMetrics = GetMetrics(response);
                    string Fmt(string name) => (string)displayMetrics.First(m => (string)m["metric"] == name)["formattedValue"];

                    var table = new Table();
                    table.AddColumns(MessageService.GetString("command-query-document_header"), MessageService.GetString("command-query-count_header"), MessageService.GetString("command-query-size_header"));

                    table.AddRow(MessageService.GetString("command-query-retrieved"), Theme.FormatTableValue(Fmt("Retrieved document count")), Theme.FormatTableValue(Fmt("Retrieved document size")));
                    table.AddRow(MessageService.GetString("command-query-output"), Theme.FormatTableValue(Fmt("Output document count")), Theme.FormatTableValue(Fmt("Output document size")));
                    AnsiConsole.Write(table);

                    table = new Table();
                    table.AddColumns(string.Empty, string.Empty, string.Empty, string.Empty);
                    table.HideHeaders();
                    table.AddRow(MessageService.GetString("command-query-document_load"), Theme.FormatTableValue(Fmt("Document load time")), MessageService.GetString("command-query-document_write"), Theme.FormatTableValue(Fmt("Document write time")));
                    table.AddRow(MessageService.GetString("command-query-query_preparation"), Theme.FormatTableValue(Fmt("Query preparation time")), MessageService.GetString("command-query-runtime_execution"), Theme.FormatTableValue(Fmt("Runtime execution time")));
                    table.AddRow(MessageService.GetString("command-query-vm_execution"), Theme.FormatTableValue(Fmt("VMExecution execution time")));
                    table.AddEmptyRow();
                    table.AddRow(MessageService.GetString("command-query-total"), Theme.FormatTableValue(Fmt("Total time")));
                    AnsiConsole.MarkupLine(MessageService.GetString("command-query-time_label"));
                    AnsiConsole.Write(table);

                    table = new Table();
                    table.AddColumns(MessageService.GetString("command-query-index_hit_ratio"), MessageService.GetString("command-query-index_lookup_time"));
                    table.AddRow(Theme.FormatTableValue(Fmt("Index hit ratio")), Theme.FormatTableValue(Fmt("Index lookup time")));
                    AnsiConsole.Write(table);

                    if (response.IndexMetrics != null)
                    {
                        var indexTable = new Table();

                        indexTable.AddColumns(MessageService.GetString("command-query-index_metrics"), MessageService.GetString("command-query-index_spec"), MessageService.GetString("command-query-index_score"));

                        var parsedIndexMetrics = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(response.IndexMetrics);

                        if (parsedIndexMetrics?.TryGetValue("UtilizedIndexes", out var utilizedIndices) == true)
                        {
                            if (utilizedIndices is JsonElement jo)
                            {
                                AddIndexTable(indexTable, MessageService.GetString("command-query-index_metric-utilized_single"), jo.GetProperty("SingleIndexes"));
                                AddIndexTable(indexTable, MessageService.GetString("command-query-index_metric-utilized_composite"), jo.GetProperty("CompositeIndexes"));
                            }
                        }

                        if (parsedIndexMetrics?.TryGetValue("PotentialIndexes", out var potentialIndices) == true)
                        {
                            if (potentialIndices is JsonElement jo)
                            {
                                AddIndexTable(indexTable, MessageService.GetString("command-query-index_metric-potential_single"), jo.GetProperty("SingleIndexes"));
                                AddIndexTable(indexTable, MessageService.GetString("command-query-index_metric-potential_composite"), jo.GetProperty("CompositeIndexes"));
                            }
                        }

                        AnsiConsole.Write(indexTable);
                    }
                }
                else
                {
                    GeneratePlainResultDocument(returnState, aggregatedDocuments);
                }

                if (ResultLimit.IsLimitReached(aggregatedDocuments.Count, effectiveMaxItemCount))
                {
                    limitReached = pageExceedsLimit || feedIterator.HasMoreResults;
                    break;
                }
            }

            if (limitReached && effectiveMaxItemCount.HasValue)
            {
                AnsiConsole.MarkupLine(MessageService.GetString("command-results-limit_reached", new Dictionary<string, object> { { "count", effectiveMaxItemCount.Value } }));
            }

            return returnState;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException e)
        {
            throw new CommandException("query", e);
        }
        catch (CommandReportedException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new CommandException("query", e);
        }
    }

    private void AppendCSV(StringBuilder sb, object metricProperty)
    {
        if (metricProperty is IDictionary dict)
        {
            this.AddCollection(sb, dict.Keys);
            sb.AppendLine();
            this.AddCollection(sb, dict.Values);
            sb.AppendLine();
        }
        else if (metricProperty is ICollection coll)
        {
            this.AddCollection(sb, coll);
        }
        else
        {
            sb.Append(metricProperty);
        }
    }

    private void AddCollection(StringBuilder sb, ICollection col)
    {
        foreach (var kv in col)
        {
            if (kv is IDictionary)
            {
                this.AppendCSV(sb, kv);
            }
            else
                if (kv is ICollection)
                {
                    this.AppendCSV(sb, kv);
                }
                else
                {
                    sb.Append(',');
                    if (kv is float || kv is double)
                    {
                        sb.AppendFormat("{0:0.00}", kv);
                    }
                    else
                    {
                        sb.Append(kv);
                    }
                }
        }
    }
}