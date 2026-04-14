//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Collections;
using System.Collections.Generic;
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
[CosmosExample("query \"SELECT * FROM c\" --database=MyDB --container=Products", Description = "Query specific database and container")]
[McpAnnotation(
    Title = "Run Query",
    ReadOnly = true,
    Idempotent = true,
    OpenWorld = true,
    Description = "Executes a Cosmos DB NoSQL query against the current container and returns matching documents. Use the cosmos://docs/nosql-query-language resource for query syntax reference.")]
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

        return await this.ExecuteQueryAsync(container, shell, token);
    }

    internal static List<JsonElement> CollectDocuments(IEnumerable<JsonElement> currentDocuments, JsonElement pageDocuments, int? maxItemCount)
    {
        var documents = currentDocuments.ToList();
        foreach (var resultDocument in pageDocuments.EnumerateArray())
        {
            if (maxItemCount >= 0 && documents.Count >= maxItemCount)
            {
                break;
            }

            documents.Add(resultDocument);
        }

        return documents;
    }

    private static void AddIndexTable(Table table, string title, JsonElement? jToken)
    {
        if (jToken == null || jToken.Value.ValueKind == JsonValueKind.Null || jToken.Value.ValueKind == JsonValueKind.Undefined)
        {
            table.AddRow($"[white]{title}[/]", $"[white]-[/]", $"[white]-[/]");
            return;
        }

        if (jToken.Value.ValueKind == JsonValueKind.Array)
        {
            var arr = jToken.Value.EnumerateArray().ToList();
            if (arr.Count == 0)
            {
                table.AddRow($"[white]{title}[/]", $"[white]-[/]", $"[white]-[/]");
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
                table.AddRow($"[white]{title}[/]", $"[white]{col1}[/]", $"[white]{col2}[/]");
            }
        }
    }

    private static void GeneratePlainResultDocument(CommandState returnState, IEnumerable<JsonElement> documents)
    {
        returnState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { items = documents.ToList() }));
    }

    private static List<Dictionary<string, object>> GetMetrics(ResponseMessage msg)
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

    private async Task<CommandState> ExecuteQueryAsync(Container container, ShellInterpreter shell, CancellationToken token)
    {
        var returnState = new CommandState();
        returnState.SetFormat(this.OutputFormat ?? Environment.GetEnvironmentVariable("COSMOS_SHELL_FORMAT"));
        var aggregatedDocuments = new List<JsonElement>();
        var queryDocuments = new List<JsonDocument>();

        try
        {
            var options = new QueryRequestOptions
            {
                PopulateIndexMetrics = true,
            };

            var effectiveMaxItemCount = ResultLimit.ResolveMaxItemCount(this.Max);
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

                if (!response.IsSuccessStatusCode)
                {
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

                    throw new CommandException("query", message);
                }

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

                var queryDocument = JsonDocument.Parse(responseContent);
                queryDocuments.Add(queryDocument);
                ShellInterpreter.WriteLine(MessageService.GetString("command-query-fetched", new Dictionary<string, object> { { "count", queryDocument.RootElement.GetProperty("_count").ToString() } }));
                var queryMetrics = response.Diagnostics.GetQueryMetrics();
                if (queryMetrics != null)
                {
                    AnsiConsole.MarkupLine(MessageService.GetString("command-query-request_charge", new Dictionary<string, object> { { "charge", queryMetrics.TotalRequestCharge.ToString() } }));
                }

                aggregatedDocuments = CollectDocuments(aggregatedDocuments, queryDocument.RootElement.GetProperty("Documents"), effectiveMaxItemCount);

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

                    table.AddRow(MessageService.GetString("command-query-retrieved"), $"[white]{Fmt("Retrieved document count")}[/]", $"[white]{Fmt("Retrieved document size")}[/]");
                    table.AddRow(MessageService.GetString("command-query-output"), $"[white]{Fmt("Output document count")}[/]", $"[white]{Fmt("Output document size")}[/]");
                    AnsiConsole.Write(table);

                    table = new Table();
                    table.AddColumns(string.Empty, string.Empty, string.Empty, string.Empty);
                    table.HideHeaders();
                    table.AddRow(MessageService.GetString("command-query-document_load"), $"[white]{Fmt("Document load time")}[/]", MessageService.GetString("command-query-document_write"), $"[white]{Fmt("Document write time")}[/]");
                    table.AddRow(MessageService.GetString("command-query-query_preparation"), $"[white]{Fmt("Query preparation time")}[/]", MessageService.GetString("command-query-runtime_execution"), $"[white]{Fmt("Runtime execution time")}[/]");
                    table.AddRow(MessageService.GetString("command-query-vm_execution"), $"[white]{Fmt("VMExecution execution time")}[/]");
                    table.AddEmptyRow();
                    table.AddRow(MessageService.GetString("command-query-total"), $"[white]{Fmt("Total time")}[/]");
                    AnsiConsole.MarkupLine(MessageService.GetString("command-query-time_label"));
                    AnsiConsole.Write(table);

                    table = new Table();
                    table.AddColumns(MessageService.GetString("command-query-index_hit_ratio"), MessageService.GetString("command-query-index_lookup_time"));
                    table.AddRow($"[white]{Fmt("Index hit ratio")}[/]", $"[white]{Fmt("Index lookup time")}[/]");
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
                    limitReached = feedIterator.HasMoreResults;
                    break;
                }
            }

            if (limitReached && effectiveMaxItemCount.HasValue)
            {
                AnsiConsole.MarkupLine(MessageService.GetString("command-results-limit_reached", new Dictionary<string, object> { { "count", effectiveMaxItemCount.Value } }));
            }

            return returnState;
        }
        catch (OperationCanceledException e)
        {
            throw new CommandException("query", e.Message);
        }
        catch (Exception e)
        {
            throw new CommandException("query", e);
        }
        finally
        {
            foreach (var doc in queryDocuments)
            {
                doc.Dispose();
            }
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