//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

internal enum ImportFormat
{
    Auto = 0,
    Jsonl = 1,
    JsonLines = 1,
    Array = 2,
    Csv = 3,
}

internal enum ImportMode
{
    Insert = 0,
    Upsert = 1,
}

[CosmosCommand("import")]
[CosmosExample("import items.jsonl", Description = "Import items from a JSON Lines file (one JSON object per line)")]
[CosmosExample("import items.json --format=array", Description = "Import items from a JSON array file")]
[CosmosExample("import items.csv", Description = "Import items from a CSV file (the header row defines property names)")]
[CosmosExample("import items.csv --partition-key=/address/city", Description = "Import CSV and nest the matching column under a nested partition key path")]
[CosmosExample("import items.jsonl --mode=upsert", Description = "Insert new items and replace any existing items with the same id")]
[CosmosExample("import items.jsonl --continue-on-error", Description = "Keep importing after individual item write failures")]
[CosmosExample("import items.jsonl --dry-run", Description = "Validate the file without writing any items")]
[McpAnnotation(
    Title = "Import Container Items",
    ReadOnly = false,
    Idempotent = false,
    OpenWorld = true,
    Description = "Bulk-loads items into a Cosmos container from a local JSON Lines, JSON array, or CSV file.")]
internal class ImportCommand : CosmosCommand
{
    private static readonly JsonDocument SuccessDocument = JsonDocument.Parse("{\"result\":\"success\"}");

    [CosmosParameter("file", RequiredErrorKey = "command-import-error-missing_file")]
    public string? File { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("mode", DefaultValue = ImportMode.Insert)]
    public ImportMode? Mode { get; init; }

    [CosmosOption("format", "f", DefaultValue = ImportFormat.Auto)]
    public ImportFormat? Format { get; init; }

    [CosmosOption("partition-key", "pk")]
    public string? PartitionKey { get; init; }

    [CosmosOption("continue-on-error", "continue")]
    public bool? ContinueOnError { get; init; }

    [CosmosOption("dry-run")]
    public bool? DryRun { get; init; }

    /// <summary>
    /// Inspects a leading chunk of file content and infers whether it is a JSON array or a
    /// JSON Lines stream. Whitespace (including comments-style BOM) is ignored. If the first
    /// non-whitespace character is '[' the file is treated as a JSON array, otherwise as
    /// JSON Lines.
    /// </summary>
    /// <param name="leadingChunk">A prefix of the file content (UTF-8 decoded).</param>
    /// <returns>The detected format.</returns>
    internal static ImportFormat DetectFormat(string leadingChunk)
    {
        if (leadingChunk == null)
        {
            return ImportFormat.JsonLines;
        }

        var startIndex = 0;
        if (leadingChunk.Length > 0 && leadingChunk[0] == '\uFEFF')
        {
            startIndex = 1;
        }

        for (var i = startIndex; i < leadingChunk.Length; i++)
        {
            var c = leadingChunk[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            return c == '[' ? ImportFormat.Array : ImportFormat.JsonLines;
        }

        return ImportFormat.JsonLines;
    }

    /// <summary>
    /// Parses a single JSON Lines line into a cloned <see cref="JsonElement"/>. The returned
    /// element is independent of any backing document and is safe to retain beyond the
    /// lifetime of the source reader.
    /// </summary>
    /// <param name="line">The line text.</param>
    /// <param name="lineNumber">The 1-based line number used in error messages.</param>
    /// <returns>The parsed element.</returns>
    /// <exception cref="CommandException">Thrown when the line is not a valid JSON document or is not an object.</exception>
    internal static JsonElement ParseJsonLine(string line, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new CommandException(
                "import",
                MessageService.GetArgsString(
                    "command-import-error-blank_line",
                    "line",
                    lineNumber));
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new CommandException(
                    "import",
                    MessageService.GetArgsString(
                        "command-import-error-not_object",
                        "line",
                        lineNumber));
            }

            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new CommandException(
                "import",
                MessageService.GetArgsString(
                    "command-import-error-invalid_line_json",
                    "line",
                    lineNumber,
                    "message",
                    ex.Message),
                ex);
        }
    }

    /// <summary>
    /// Enumerates items from a JSON Lines reader. Blank lines are skipped; non-object lines
    /// surface as a <see cref="CommandException"/> from the consumer's <c>await foreach</c>.
    /// </summary>
    /// <param name="reader">The source reader.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>An async sequence of (1-based line number, item) tuples.</returns>
    internal static async IAsyncEnumerable<(int LineNumber, JsonElement Item)> EnumerateJsonLinesAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        var lineNumber = 0;
        while (await reader.ReadLineAsync(token) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            yield return (lineNumber, ParseJsonLine(line, lineNumber));
        }
    }

    /// <summary>
    /// Enumerates items from a JSON array stream using a streaming deserializer that does
    /// not materialize the whole array.
    /// </summary>
    /// <param name="stream">The source stream.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>An async sequence of (1-based item index, item) tuples.</returns>
    internal static async IAsyncEnumerable<(int LineNumber, JsonElement Item)> EnumerateArrayAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken token)
    {
        var index = 0;
        await foreach (var element in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: token))
        {
            index++;
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new CommandException(
                    "import",
                    MessageService.GetArgsString(
                        "command-import-error-not_object",
                        "line",
                        index));
            }

            yield return (index, element.Clone());
        }
    }

    /// <summary>
    /// Parses a partition key path (e.g. <c>/address/city</c>) into its individual
    /// segments. Returns <see langword="null"/> when no usable path is supplied.
    /// </summary>
    /// <param name="path">The partition key path.</param>
    /// <returns>The path segments, or <see langword="null"/>.</returns>
    internal static string[]? ParsePartitionKeySegments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? null : segments;
    }

    /// <summary>
    /// Parses CSV text into records, honoring RFC 4180 quoting rules: fields may be quoted
    /// with double quotes, embedded quotes are escaped by doubling, and quoted fields may
    /// contain the separator and newlines. Blank lines are skipped.
    /// </summary>
    /// <param name="content">The full CSV text.</param>
    /// <param name="separator">The field separator.</param>
    /// <returns>A list of records, each a list of field values.</returns>
    internal static List<List<string>> ParseCsv(string content, char separator)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var hasContent = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
                hasContent = true;
            }
            else if (c == separator)
            {
                record.Add(field.ToString());
                field.Clear();
                hasContent = true;
            }
            else if (c == '\r')
            {
                // Ignored; line breaks are handled on '\n'.
            }
            else if (c == '\n')
            {
                record.Add(field.ToString());
                field.Clear();
                if (hasContent || record.Count > 1)
                {
                    records.Add(record);
                }

                record = new List<string>();
                hasContent = false;
            }
            else
            {
                field.Append(c);
                hasContent = true;
            }
        }

        if (hasContent || field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Builds a JSON object from a CSV header row and a value row. Every column becomes a
    /// top-level string property. When <paramref name="partitionKeySegments"/> describes a
    /// nested path, the matching leaf column is relocated under that nested path.
    /// </summary>
    /// <param name="headers">The header field names.</param>
    /// <param name="values">The row values.</param>
    /// <param name="partitionKeySegments">Optional partition key path segments.</param>
    /// <returns>The constructed JSON object element.</returns>
    internal static JsonElement BuildCsvObject(IReadOnlyList<string> headers, IReadOnlyList<string> values, string[]? partitionKeySegments)
    {
        var root = new JsonObject();
        for (var i = 0; i < headers.Count; i++)
        {
            var name = headers[i];
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var value = i < values.Count ? values[i] : string.Empty;
            root[name] = JsonValue.Create(value);
        }

        if (partitionKeySegments is { Length: > 1 })
        {
            var leaf = partitionKeySegments[^1];
            if (root.ContainsKey(leaf))
            {
                var pkValue = root[leaf];
                root.Remove(leaf);

                var current = root;
                for (var s = 0; s < partitionKeySegments.Length - 1; s++)
                {
                    var seg = partitionKeySegments[s];
                    if (current[seg] is JsonObject existing)
                    {
                        current = existing;
                    }
                    else if (current.ContainsKey(seg))
                    {
                        // An intermediate segment already holds a scalar (or
                        // array) value from another CSV column. Nesting under it
                        // would silently discard that value, so fail loudly
                        // instead of corrupting the imported data.
                        throw new CommandException(
                            "import",
                            MessageService.GetArgsString(
                                "command-import-error-csv_pk_conflict",
                                "column",
                                seg,
                                "path",
                                "/" + string.Join("/", partitionKeySegments)));
                    }
                    else
                    {
                        var next = new JsonObject();
                        current[seg] = next;
                        current = next;
                    }
                }

                current[leaf] = pkValue;
            }
        }

        return JsonSerializer.SerializeToElement(root);
    }

    /// <summary>
    /// Enumerates items from a CSV file. The first record is treated as the header row.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <param name="partitionKeySegments">Optional partition key path segments.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>An async sequence of (1-based file line number, item) tuples.</returns>
    internal static async IAsyncEnumerable<(int LineNumber, JsonElement Item)> EnumerateCsvAsync(
        string filePath,
        string[]? partitionKeySegments,
        [EnumeratorCancellation] CancellationToken token)
    {
        var content = await System.IO.File.ReadAllTextAsync(filePath, token);
        var records = ParseCsv(content, ShellInterpreter.CSVSeparator);
        if (records.Count == 0)
        {
            yield break;
        }

        var headers = records[0];
        for (var r = 1; r < records.Count; r++)
        {
            yield return (r + 1, BuildCsvObject(headers, records[r], partitionKeySegments));
        }
    }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.File))
        {
            throw new CommandException("import", MessageService.GetString("command-import-error-missing_file"));
        }

        var filePath = this.File!;
        if (!System.IO.File.Exists(filePath))
        {
            throw new CommandException(
                "import",
                MessageService.GetArgsString("command-import-error-file_not_found", "file", filePath));
        }

        var dryRun = this.DryRun == true;

        Container? container = null;
        if (!dryRun)
        {
            if (shell.State is not ConnectedState connectedState)
            {
                throw new NotConnectedException("import");
            }

            var (_, _, resolvedContainer) = await ResolveContainerAsync(
                connectedState.Client,
                shell.State,
                this.Database,
                this.Container,
                "import",
                token);
            container = resolvedContainer;
        }

        var format = await ResolveFormatAsync(filePath, this.Format ?? ImportFormat.Auto, token);
        var mode = this.Mode ?? ImportMode.Insert;
        var continueOnError = this.ContinueOnError == true;
        var partitionKeySegments = ParsePartitionKeySegments(this.PartitionKey);

        var (successCount, failCount, charge) = await ExecuteImportAsync(filePath, format, mode, container, continueOnError, dryRun, partitionKeySegments, token);

        if (dryRun)
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-import-dry-run-success",
                "count",
                successCount));
        }
        else if (failCount == 0)
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-import-success",
                "count",
                successCount,
                "charge",
                charge.ToString("F2", CultureInfo.InvariantCulture)));
        }
        else if (successCount > 0)
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-import-success-partial",
                "success",
                successCount,
                "failed",
                failCount,
                "charge",
                charge.ToString("F2", CultureInfo.InvariantCulture)));
        }
        else
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-import-all-failed",
                "count",
                failCount));
        }

        if (failCount > 0)
        {
            throw new CommandException(
                "import",
                MessageService.GetArgsString(
                    "command-import-error-some_failed",
                    "failed",
                    failCount,
                    "total",
                    successCount + failCount));
        }

        return new CommandState
        {
            Result = new ShellJson(SuccessDocument.RootElement.Clone()),
        };
    }

    private static async Task<ImportFormat> ResolveFormatAsync(string filePath, ImportFormat requested, CancellationToken token)
    {
        if (requested != ImportFormat.Auto)
        {
            return requested;
        }

        if (string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ImportFormat.Csv;
        }

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[1024];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
        var leading = Encoding.UTF8.GetString(buffer, 0, read);
        return DetectFormat(leading);
    }

    private static async Task<(int Success, int Failed, double Charge)> ExecuteImportAsync(
        string filePath,
        ImportFormat format,
        ImportMode mode,
        Container? container,
        bool continueOnError,
        bool dryRun,
        string[]? partitionKeySegments,
        CancellationToken token)
    {
        var success = 0;
        var failed = 0;
        var charge = 0.0;

        FileStream? stream = null;
        StreamReader? lineReader = null;
        IAsyncEnumerable<(int LineNumber, JsonElement Item)> source;
        try
        {
            if (format == ImportFormat.Csv)
            {
                source = EnumerateCsvAsync(filePath, partitionKeySegments, token);
            }
            else
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (format == ImportFormat.Array)
                {
                    source = EnumerateArrayAsync(stream, token);
                }
                else
                {
                    lineReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    source = EnumerateJsonLinesAsync(lineReader, token);
                }
            }

            await foreach (var (lineNumber, item) in source.WithCancellation(token))
            {
                if (dryRun || container is null)
                {
                    success++;
                    continue;
                }

                try
                {
                    var response = mode == ImportMode.Upsert
                        ? await container.UpsertItemAsync(item, cancellationToken: token)
                        : await container.CreateItemAsync(item, cancellationToken: token);
                    charge += response.RequestCharge;

                    var ok = mode == ImportMode.Upsert
                        ? response.StatusCode == System.Net.HttpStatusCode.OK || response.StatusCode == System.Net.HttpStatusCode.Created
                        : response.StatusCode == System.Net.HttpStatusCode.Created;

                    if (ok)
                    {
                        success++;
                    }
                    else
                    {
                        failed++;
                        ShellInterpreter.WriteLine(MessageService.GetArgsString(
                            "command-import-error-item_status",
                            "line",
                            lineNumber,
                            "status",
                            response.StatusCode.ToString()));
                        if (!continueOnError)
                        {
                            break;
                        }
                    }
                }
                catch (CosmosException ce)
                {
                    failed++;
                    charge += ce.RequestCharge;
                    ShellInterpreter.WriteLine(MessageService.GetArgsString(
                        "command-import-error-item_failed",
                        "line",
                        lineNumber,
                        "status",
                        ce.StatusCode.ToString(),
                        "message",
                        CommandException.GetDisplayMessage(ce)));
                    if (!continueOnError)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            lineReader?.Dispose();
            if (stream != null)
            {
                await stream.DisposeAsync();
            }
        }

        return (success, failed, charge);
    }
}
