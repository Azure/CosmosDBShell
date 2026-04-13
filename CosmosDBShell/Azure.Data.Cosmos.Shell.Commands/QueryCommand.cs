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
        var charge = queryMetrics?.TotalRequestCharge ?? 0;
        var cumulative = queryMetrics?.CumulativeMetrics;
        return [
            new Dictionary<string, object>
            {
                { "metric", "Request Charge" },
                { "value",  charge },
                { "formattedValue", $"{charge} RUs" },
                { "tooltip", "Request Charge" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Retrieved document count" },
                { "value",  cumulative?.RetrievedDocumentCount ?? 0 },
                { "formattedValue", $"{cumulative?.RetrievedDocumentCount ?? 0}" },
                { "tooltip", "Total number of retrieved documents" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Retrieved document size" },
                { "value",  cumulative?.RetrievedDocumentSize ?? 0 },
                { "formattedValue", $"{cumulative?.RetrievedDocumentSize ?? 0} bytes" },
                { "tooltip", "Total size of retrieved documents in bytes" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Output document count" },
                { "value",  cumulative?.OutputDocumentCount ?? 0 },
                { "formattedValue", $"{cumulative?.OutputDocumentCount ?? 0}" },
                { "tooltip", "Total Number of output documents" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Output document size" },
                { "value",  cumulative?.OutputDocumentSize ?? 0 },
                { "formattedValue", $"{cumulative?.OutputDocumentSize ?? 0} bytes" },
                { "tooltip", "Total size of output documents in bytes" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Index hit ratio" },
                { "value",  cumulative?.IndexHitRatio ?? 0 },
                { "formattedValue", $"{cumulative?.IndexHitRatio ?? 0}" },
                { "tooltip", "The index hit ratio by query in the Azure Cosmos database service. Value is within the range [0,1]." },
            },

            new Dictionary<string, object>
            {
                { "metric", "Index lookup time" },
                { "value",  cumulative?.IndexLookupTime.TotalMilliseconds ?? 0 },
                { "formattedValue", $"{cumulative?.IndexLookupTime.TotalMilliseconds ?? 0} ms" },
                { "tooltip", "The query index lookup time in the Azure Cosmos database service." },
            },

            new Dictionary<string, object>
            {
                { "metric", "Document load time" },
                { "value",  cumulative?.DocumentLoadTime.TotalMilliseconds ?? 0 },
                { "formattedValue", $"{cumulative?.DocumentLoadTime.TotalMilliseconds ?? 0} ms" },
                { "tooltip", "Time spent in loading documents" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Runtime execution time" },
                { "value",  cumulative?.RuntimeExecutionTime.TotalMilliseconds ?? 0 },
                { "formattedValue", $"{cumulative?.RuntimeExecutionTime.TotalMilliseconds ?? 0} ms" },
                { "tooltip", "The query runtime execution time during query in the Azure Cosmos database\r\n    //     service." },
            },

            new Dictionary<string, object>
            {
                { "metric", "VMExecution execution time" },
                { "value",  cumulative?.VMExecutionTime.TotalMilliseconds ?? 0 },
                { "formattedValue", $"{cumulative?.VMExecutionTime.TotalMilliseconds ?? 0} ms" },
                { "tooltip", "Total time spent executing the virtual machine" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Query preparation time " },
                { "value",  cumulative?.QueryPreparationTime.TotalMilliseconds ?? 0 },
                { "formattedValue", $"{cumulative?.QueryPreparationTime.TotalMilliseconds ?? 0} ms" },
                { "tooltip", "The query preparation time in the Azure Cosmos database service." },
            },

            new Dictionary<string, object>
            {
                { "metric", "Document write time" },
                { "value",  cumulative?.DocumentWriteTime.TotalMilliseconds ?? 0 },
                { "formattedValue", $"{cumulative?.DocumentWriteTime.TotalMilliseconds ?? 0} ms" },
                { "tooltip", "Time spent to write query result set to response buffer" },
            },

            new Dictionary<string, object>
            {
                { "metric", "Total time" },
                { "value",  cumulative?.TotalTime.TotalMilliseconds ?? 0 },
                { "formattedValue", $"{cumulative?.TotalTime.TotalMilliseconds ?? 0} ms" },
                { "tooltip", "The total query time in the Azure Cosmos database service." },
            },
        ];
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.ToString();
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

            if (this.Bucket.HasValue)
            {
                options.ThroughputBucket = this.Bucket.Value;
            }

            if (string.IsNullOrWhiteSpace(this.Query))
            {
                throw new CommandException("query", MessageService.GetString("command-query-error-empty_query"));
            }

            using var feedIterator = container.GetItemQueryStreamIterator(this.Query, null, options);

            var opt = new QueryRequestOptions
            {
                PopulateIndexMetrics = true,
            };
            if (this.Max.HasValue)
            {
                opt.MaxItemCount = this.Max.Value;
            }

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
                    AnsiConsole.MarkupLine(MessageService.GetString("command-query-request_charge", new Dictionary<string, object> { { "charge", (queryMetrics?.TotalRequestCharge ?? 0).ToString() } }));
                }

                aggregatedDocuments = CollectDocuments(aggregatedDocuments, queryDocument.RootElement.GetProperty("Documents"), opt.MaxItemCount);

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
                                { "indexMetrics", parsedIndexMetrics ?? [] },
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

                    var cumulativeMetrics = queryMetrics?.CumulativeMetrics;
                    if (cumulativeMetrics != null)
                    {
                        var table = new Table();
                        table.AddColumns(MessageService.GetString("command-query-document_header"), MessageService.GetString("command-query-count_header"), MessageService.GetString("command-query-size_header"));

                        table.AddRow(MessageService.GetString("command-query-retrieved"), $"[white]{cumulativeMetrics.RetrievedDocumentCount}[/]", $"[white]{cumulativeMetrics.RetrievedDocumentSize}[/]");
                        table.AddRow(MessageService.GetString("command-query-output"), $"[white]{cumulativeMetrics.OutputDocumentCount}[/]", $"[white]{cumulativeMetrics.OutputDocumentSize}[/]");
                        AnsiConsole.Write(table);

                        table = new Table();
                        table.AddColumns(string.Empty, string.Empty, string.Empty, string.Empty);
                        table.HideHeaders();
                        table.AddRow(MessageService.GetString("command-query-document_load"), $"[white]{FormatTime(cumulativeMetrics.DocumentLoadTime)}[/]", MessageService.GetString("command-query-document_write"), $"[white]{FormatTime(cumulativeMetrics.DocumentWriteTime)}[/]");
                        table.AddRow(MessageService.GetString("command-query-query_preparation"), $"[white]{FormatTime(cumulativeMetrics.QueryPreparationTime)}[/]", MessageService.GetString("command-query-runtime_execution"), $"[white]{FormatTime(cumulativeMetrics.RuntimeExecutionTime)}[/]");
                        table.AddRow(MessageService.GetString("command-query-vm_execution"), $"[white]{FormatTime(cumulativeMetrics.VMExecutionTime)}[/]");
                        table.AddEmptyRow();
                        table.AddRow(MessageService.GetString("command-query-total"), $"[white]{FormatTime(cumulativeMetrics.TotalTime)}[/]");
                        AnsiConsole.MarkupLine(MessageService.GetString("command-query-time_label"));
                        AnsiConsole.Write(table);

                        table = new Table();
                        table.AddColumns(MessageService.GetString("command-query-index_hit_ratio"), MessageService.GetString("command-query-index_lookup_time"));
                        table.AddRow($"[white]{cumulativeMetrics.IndexHitRatio}[/]", $"[white]{FormatTime(cumulativeMetrics.IndexLookupTime)}[/]");
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
                                    AddIndexTable(indexTable, MessageService.GetString("command-query-index_metric-potential_single"), jo.GetProperty("CompositeIndexes"));
                                }
                            }

                            if (parsedIndexMetrics?.TryGetValue("PotentialIndexes", out var potentialIndices) == true)
                            {
                                if (potentialIndices is JsonElement jo)
                                {
                                    AddIndexTable(indexTable, MessageService.GetString("command-query-index_metric-utilized_composite"), jo.GetProperty("SingleIndexes"));
                                    AddIndexTable(indexTable, MessageService.GetString("command-query-index_metric-potential_composite"), jo.GetProperty("CompositeIndexes"));
                                }
                            }

                            AnsiConsole.Write(indexTable);
                        }
                    }
                }
                else
                {
                    GeneratePlainResultDocument(returnState, aggregatedDocuments);
                }

                if (opt.MaxItemCount >= 0 && aggregatedDocuments.Count >= opt.MaxItemCount)
                {
                    break;
                }
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