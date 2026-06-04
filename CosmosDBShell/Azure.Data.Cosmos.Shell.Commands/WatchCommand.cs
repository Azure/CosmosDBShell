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
using Spectre.Console;

[CosmosCommand("watch", Aliases = ["tail"])]
[CosmosExample("watch", Description = "Tail new changes in the current container as they arrive")]
[CosmosExample("watch --from-beginning", Description = "Replay the change feed from the beginning of the container")]
[CosmosExample("watch --partition-key=myKey --max=100", Description = "Watch a single partition key and stop after 100 changes")]
[CosmosExample("watch --database=MyDB --container=Products", Description = "Watch a specific database and container")]
[McpAnnotation(Restricted = true, ReadOnly = true)]
internal class WatchCommand : CosmosCommand
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [CosmosOption("from-beginning", "b")]
    public bool FromBeginning { get; init; }

    [CosmosOption("partition-key", "pk")]
    public string? PartitionKey { get; init; }

    [CosmosOption("max", "m")]
    public int? Max { get; init; }

    [CosmosOption("format", "f")]
    public string? OutputFormat { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(shell);

        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("watch");
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "watch",
            token);

        // A tail is long-running and must not be killed by the per-command
        // timeout. Switch to the interruptible (no-timeout) cancellation source
        // so the loop runs until the user presses Ctrl+C or --max is reached.
        var watchToken = ShellInterpreter.UserCancellationTokenSource.Token;

        return await this.WatchAsync(shell, container, watchToken);
    }

    /// <summary>
    /// Builds the change feed start position from the command options. When a
    /// partition key is supplied the feed is scoped to that single partition,
    /// otherwise it spans the whole container.
    /// </summary>
    internal static ChangeFeedStartFrom BuildStartFrom(bool fromBeginning, string? partitionKey)
    {
        if (!string.IsNullOrEmpty(partitionKey))
        {
            var feedRange = FeedRange.FromPartitionKey(CreatePartitionKeyFromArgument(partitionKey));
            return fromBeginning ? ChangeFeedStartFrom.Beginning(feedRange) : ChangeFeedStartFrom.Now(feedRange);
        }

        return fromBeginning ? ChangeFeedStartFrom.Beginning() : ChangeFeedStartFrom.Now();
    }

    /// <summary>
    /// Extracts the documents from a change feed response body. A successful
    /// change feed response carries a <c>Documents</c> array, identical in shape
    /// to a query response.
    /// </summary>
    internal static IReadOnlyList<JsonElement> ParseChangeFeedDocuments(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("Documents", out var documents) || documents.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<JsonElement>();
        foreach (var element in documents.EnumerateArray())
        {
            list.Add(element.Clone());
        }

        return list;
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadChangeFeedDocumentsAsync(ResponseMessage response, CancellationToken token)
    {
        if (response.Content == null)
        {
            return [];
        }

        using var reader = new StreamReader(response.Content);
        var content = await reader.ReadToEndAsync(token);
        return ParseChangeFeedDocuments(content);
    }

    private async Task<CommandState> WatchAsync(ShellInterpreter shell, Container container, CancellationToken token)
    {
        ChangeFeedStartFrom startFrom;
        try
        {
            startFrom = BuildStartFrom(this.FromBeginning, this.PartitionKey);
        }
        catch (JsonException ex)
        {
            throw new CommandException("watch", MessageService.GetString("command-patch-error-invalid_pk_json"), ex);
        }

        var redirected = !string.IsNullOrEmpty(shell.StdOutRedirect);
        using var iterator = container.GetChangeFeedStreamIterator(startFrom, ChangeFeedMode.Incremental);

        var max = this.Max is > 0 ? this.Max : null;
        var collected = (max.HasValue || redirected) ? new List<JsonElement>() : null;
        var count = 0;

        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-watch-started", "container", Theme.ContainerNamePromt(container.Id)));

        try
        {
            while (!token.IsCancellationRequested)
            {
                using var response = await iterator.ReadNextAsync(token);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    // Caught up with the feed; wait before polling for more.
                    await Task.Delay(PollInterval, token);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new CommandException("watch", MessageService.GetString("command-watch-error-request_failed", new Dictionary<string, object>
                    {
                        { "statusCode", (int)response.StatusCode },
                        { "status", response.StatusCode },
                    }));
                }

                var documents = await ReadChangeFeedDocumentsAsync(response, token);
                var limitReached = false;
                foreach (var element in documents)
                {
                    if (!redirected)
                    {
                        AnsiConsole.MarkupLine(JsonOutputHighlighter.BuildMarkup(element));
                    }

                    collected?.Add(element);
                    count++;

                    if (max.HasValue && count >= max.Value)
                    {
                        limitReached = true;
                        break;
                    }
                }

                if (limitReached)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C: stop tailing and report what was seen so far.
        }

        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-watch-stopped", "count", count.ToString()));

        var result = new CommandState
        {
            IsPrinted = !redirected,
        };
        result.SetFormat(this.OutputFormat ?? Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT"));
        if (collected != null)
        {
            result.Result = new ShellJson(JsonSerializer.SerializeToElement(new { changes = collected }));
        }

        return result;
    }
}
