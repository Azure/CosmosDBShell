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
    Csv = 2,
}

[CosmosCommand("export")]
[CosmosExample("export items.jsonl", Description = "Export every item in the current container as JSON Lines")]
[CosmosExample("export items.jsonl --query=\"SELECT * FROM c WHERE c.status = 'active'\"", Description = "Export the results of a query")]
[CosmosExample("export items.json --format=array --force", Description = "Export as a JSON array, overwriting an existing file")]
[CosmosExample("export items.csv --format=csv", Description = "Export items as CSV (one column per top-level property)")]
[CosmosExample("export items.jsonl --db=MyDB --con=Products --max=1000", Description = "Export up to 1000 items from a specific database and container")]
[McpAnnotation(
    Title = "Export Container Items",
    ReadOnly = false,
    Idempotent = false,
    OpenWorld = true,
    Description = "Streams items from a Cosmos container into a local JSON Lines, JSON array, or CSV file.")]
internal class ExportCommand : CosmosCommand
{
    private const string DefaultQuery = "SELECT * FROM c";

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

        var (count, charge) = await ExecuteExportAsync(container, query, max, format, filePath, this.Force == true, token);

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
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                type = "export",
                file = filePath,
                exported = count,
                requestCharge = charge,
            })),
            RequestCharge = charge,
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
            if (writer.BytesPending >= 64 * 1024)
            {
                await writer.FlushAsync(token);
            }

            count++;
        }

        writer.WriteEndArray();
        await writer.FlushAsync(token);
        return count;
    }

    /// <summary>
    /// Writes a sequence of items to <paramref name="writer"/> as CSV. The header row is the
    /// union of all top-level property names (in first-seen order); each subsequent row
    /// contains the corresponding values. Nested objects and arrays are written as compact
    /// JSON. Items are spooled to disk to compute the column set.
    /// </summary>
    /// <param name="items">The items to write.</param>
    /// <param name="writer">The destination writer.</param>
    /// <param name="separator">The field separator.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The number of data rows written.</returns>
    internal static async Task<int> WriteCsvAsync(IAsyncEnumerable<JsonElement> items, TextWriter writer, char separator, CancellationToken token)
    {
        var spoolOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            spoolOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var spool = new FileStream(Path.Join(Path.GetTempPath(), $"cosmos-csv-{Guid.NewGuid():N}.tmp"), spoolOptions);
        using var spoolWriter = new StreamWriter(spool, new UTF8Encoding(false), leaveOpen: true);
        var headers = new List<string>();
        var headerSet = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var item in items.WithCancellation(token))
        {
            await spoolWriter.WriteLineAsync(SerializeJsonLine(item).AsMemory(), token);
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var prop in item.EnumerateObject())
            {
                if (headerSet.Add(prop.Name))
                {
                    headers.Add(prop.Name);
                }
            }
        }

        await spoolWriter.FlushAsync(token);
        spool.Position = 0;
        using var spoolReader = new StreamReader(spool, leaveOpen: true);
        var sb = new StringBuilder();
        if (headers.Count > 0)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(separator);
                }

                sb.Append(CommandState.EscapeCSV(headers[i]));
            }

            await writer.WriteLineAsync(sb.ToString().AsMemory(), token);
        }

        var count = 0;
        while (await spoolReader.ReadLineAsync(token) is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var item = document.RootElement;
            sb.Clear();
            for (var i = 0; i < headers.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(separator);
                }

                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(headers[i], out var value))
                {
                    var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
                    sb.Append(CommandState.EscapeCSV(text));
                }
                else
                {
                    sb.Append(CommandState.EscapeCSV(string.Empty));
                }
            }

            await writer.WriteLineAsync(sb.ToString().AsMemory(), token);
            count++;
        }

        await writer.FlushAsync(token);
        return count;
    }

    private static async Task<(int Count, double Charge)> ExecuteExportAsync(
        Container container,
        string query,
        int? max,
        ExportFormat format,
        string filePath,
        bool overwrite,
        CancellationToken token)
    {
        var options = new QueryRequestOptions();
        if (max is int explicitMax && explicitMax > 0)
        {
            options.MaxItemCount = explicitMax;
        }

        try
        {
            var totalCharge = 0.0;
            using var iterator = container.GetItemQueryIterator<JsonElement>(query, requestOptions: options);
            var count = await WriteFileAsync(
                EnumerateAsync(iterator, max, charge => totalCharge += charge, token), format, filePath, overwrite, token);
            return (count, totalCharge);
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

    internal static async Task<int> WriteFileAsync(
        IAsyncEnumerable<JsonElement> items,
        ExportFormat format,
        string filePath,
        bool overwrite,
        CancellationToken token)
    {
        var destination = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Join(directory, $".cosmos-export-{Guid.NewGuid():N}.tmp");
        try
        {
            int count;
            var fileOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            };
            if (!OperatingSystem.IsWindows())
            {
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = new FileStream(temporary, fileOptions))
            {
                if (format == ExportFormat.Array)
                {
                    count = await WriteArrayAsync(items, stream, token);
                }
                else
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.NewLine = "\n";
                    count = format == ExportFormat.Csv
                        ? await WriteCsvAsync(items, writer, ShellInterpreter.CSVSeparator, token)
                        : await WriteJsonLinesAsync(items, writer, token);
                }
            }

            token.ThrowIfCancellationRequested();
            System.IO.File.Move(temporary, destination, overwrite);
            return count;
        }
        finally
        {
            DeleteTemporaryFile(temporary);
        }
    }

    internal static void DeleteTemporaryFile(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Export temporary-file cleanup failed ({0}).", exception.GetType().Name);
        }
    }

    internal static async IAsyncEnumerable<JsonElement> EnumerateAsync(
        FeedIterator<JsonElement> iterator,
        int? max,
        Action<double> recordCharge,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var emitted = 0;
        while ((!max.HasValue || emitted < max.Value) && iterator.HasMoreResults)
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
