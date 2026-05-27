//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

internal enum ExportFormat
{
    Jsonl = 0,
    JsonLines = 0,
    Array = 1,
}

[CosmosCommand("export")]
[CosmosExample("export items.jsonl", Description = "Export every item in the current container as JSON Lines")]
[CosmosExample("export items.jsonl --query=\"SELECT * FROM c WHERE c.status = 'active'\"", Description = "Export the results of a query")]
[CosmosExample("export items.json --format=array --force", Description = "Export as a JSON array, overwriting an existing file")]
[CosmosExample("export items.jsonl --db=MyDB --con=Products --max=1000", Description = "Export up to 1000 items from a specific database and container")]
[McpAnnotation(
    Title = "Export Container Items",
    ReadOnly = false,
    Idempotent = false,
    OpenWorld = true,
    Description = "Streams items from a Cosmos container into a local JSON Lines or JSON array file.")]
internal class ExportCommand : CosmosCommand
{
    private const string DefaultQuery = "SELECT * FROM c";

    private static readonly JsonDocument SuccessDocument = JsonDocument.Parse("{\"result\":\"success\"}");

    [CosmosParameter("file", RequiredErrorKey = "command-export-error-missing_file")]
    public string? File { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("query", "q")]
    public string? Query { get; init; }

    [CosmosOption("max", "m")]
    public int? Max { get; init; }

    [CosmosOption("format", "f", DefaultValue = ExportFormat.JsonLines)]
    public ExportFormat? Format { get; init; }

    [CosmosOption("force")]
    public bool? Force { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.File))
        {
            throw new CommandException("export", MessageService.GetString("command-export-error-missing_file"));
        }

        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("export");
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "export",
            token);

        var filePath = this.File!;
        if (System.IO.File.Exists(filePath) && this.Force != true)
        {
            throw new CommandException(
                "export",
                MessageService.GetArgsString("command-export-error-file_exists", "file", filePath));
        }

        var query = string.IsNullOrWhiteSpace(this.Query) ? DefaultQuery : this.Query!;
        var max = ResultLimit.ResolveMaxItemCount(this.Max, defaultMaxItemCount: null);
        var format = this.Format ?? ExportFormat.JsonLines;

        var (count, charge) = await ExecuteExportAsync(container, query, max, format, filePath, token);

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-export-success",
            "count",
            count,
            "file",
            filePath,
            "charge",
            charge.ToString("F2", CultureInfo.InvariantCulture)));

        return new CommandState
        {
            Result = new ShellJson(SuccessDocument.RootElement.Clone()),
        };
    }

    /// <summary>
    /// Serializes a single JSON element as a single-line UTF-8 JSON string suitable for
    /// inclusion in a JSON Lines file. The element is fully re-serialized; any whitespace
    /// or newlines from the source document are stripped.
    /// </summary>
    /// <param name="element">The element to serialize.</param>
    /// <returns>A compact JSON representation without newlines.</returns>
    internal static string SerializeJsonLine(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Writes a sequence of items to <paramref name="writer"/> as JSON Lines (one compact
    /// JSON document per line) and returns the number of items written.
    /// </summary>
    /// <param name="items">The items to write.</param>
    /// <param name="writer">The destination writer.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The number of items written.</returns>
    internal static async Task<int> WriteJsonLinesAsync(IAsyncEnumerable<JsonElement> items, TextWriter writer, CancellationToken token)
    {
        var count = 0;
        await foreach (var item in items.WithCancellation(token))
        {
            await writer.WriteLineAsync(SerializeJsonLine(item).AsMemory(), token);
            count++;
        }

        await writer.FlushAsync(token);
        return count;
    }

    /// <summary>
    /// Writes a sequence of items to <paramref name="stream"/> as a single JSON array and
    /// returns the number of items written. Items are streamed; the entire array is never
    /// materialized in memory.
    /// </summary>
    /// <param name="items">The items to write.</param>
    /// <param name="stream">The destination stream.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The number of items written.</returns>
    internal static async Task<int> WriteArrayAsync(IAsyncEnumerable<JsonElement> items, Stream stream, CancellationToken token)
    {
        var count = 0;
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();
        await foreach (var item in items.WithCancellation(token))
        {
            item.WriteTo(writer);
            count++;
        }

        writer.WriteEndArray();
        await writer.FlushAsync(token);
        return count;
    }

    private static async Task<(int Count, double Charge)> ExecuteExportAsync(
        Container container,
        string query,
        int? max,
        ExportFormat format,
        string filePath,
        CancellationToken token)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new QueryRequestOptions();
        if (max is int explicitMax && explicitMax > 0)
        {
            options.MaxItemCount = explicitMax;
        }

        try
        {
            var totalCharge = 0.0;
            var iterator = container.GetItemQueryIterator<JsonElement>(query, requestOptions: options);

            if (format == ExportFormat.Array)
            {
                await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                var count = await WriteArrayAsync(EnumerateAsync(iterator, max, charge => totalCharge += charge, token), stream, token);
                return (count, totalCharge);
            }
            else
            {
                await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.NewLine = "\n";
                var count = await WriteJsonLinesAsync(EnumerateAsync(iterator, max, charge => totalCharge += charge, token), writer, token);
                return (count, totalCharge);
            }
        }
        catch (CosmosException ce)
        {
            throw new CommandException(
                "export",
                MessageService.GetArgsString(
                    "command-export-error-query_failed",
                    "status",
                    ce.StatusCode.ToString(),
                    "message",
                    CommandException.GetDisplayMessage(ce)),
                ce);
        }
    }

    private static async IAsyncEnumerable<JsonElement> EnumerateAsync(
        FeedIterator<JsonElement> iterator,
        int? max,
        Action<double> recordCharge,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var emitted = 0;
        while (iterator.HasMoreResults)
        {
            FeedResponse<JsonElement> response;
            try
            {
                response = await iterator.ReadNextAsync(token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            recordCharge(response.RequestCharge);
            foreach (var item in response)
            {
                if (max.HasValue && emitted >= max.Value)
                {
                    yield break;
                }

                yield return item;
                emitted++;
            }
        }
    }
}
