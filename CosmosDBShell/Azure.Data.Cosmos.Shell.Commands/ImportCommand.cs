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
using CsvHelper;
using CsvHelper.Configuration;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

internal enum ImportFormat
{
    Json,
    Csv,
}

internal enum ImportMode
{
    Insert = 0,
    Upsert = 1,
}

[CosmosCommand("import")]
[CosmosExample("import items.json", DescriptionKey = "command-import-example-1")]
[CosmosExample("import items.jsonl", DescriptionKey = "command-import-example-2")]
[CosmosExample("import items.csv", DescriptionKey = "command-import-example-3")]
[CosmosExample("import items.csv --partition-key=/address/city", DescriptionKey = "command-import-example-4")]
[CosmosExample("import items.json --mode=upsert", DescriptionKey = "command-import-example-5")]
[CosmosExample("import items.json --continue-on-error", DescriptionKey = "command-import-example-6")]
[CosmosExample("import items.json --dry-run", DescriptionKey = "command-import-example-7")]
[CosmosExample("import items.json --concurrency=32", DescriptionKey = "command-import-example-8")]
[McpAnnotation(
    Title = "Import Container Items",
    ReadOnly = false,
    Idempotent = false,
    OpenWorld = true,
    Description = "Bulk-loads items into a Cosmos container from a local JSON array, a single JSON object, an automatically detected sequence of JSON objects (including JSON Lines), or a CSV file. The format option controls the command result, not the input file. Dry runs validate input and item IDs offline, without accessing the target container; they do not verify partition keys, permissions, conflicts, or other service constraints.")]
internal class ImportCommand : CosmosCommand
{
    internal const int DefaultConcurrency = 16;

    private const int MaxItemIdByteLength = 1023;

    [CosmosParameter("file", RequiredErrorKey = "command-import-error-missing_file")]
    public string? File { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("mode", DefaultValue = ImportMode.Insert)]
    public ImportMode? Mode { get; init; }

    [CosmosOption("format", "f")]
    public string? Format { get; init; }

    [CosmosOption("concurrency", DefaultValue = DefaultConcurrency, MinimumValue = 1)]
    public int? Concurrency { get; init; }

    [CosmosOption("partition-key", "pk")]
    public string? PartitionKey { get; init; }

    [CosmosOption("continue-on-error", "continue")]
    public bool? ContinueOnError { get; init; }

    [CosmosOption("dry-run")]
    public bool? DryRun { get; init; }

    /// <summary>
    /// Resolves the effective input format. Selects CSV for a <c>.csv</c> extension and
    /// otherwise JSON whose structure is detected from its contents.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>The effective format.</returns>
    internal static ImportFormat ResolveFormat(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase)
            ? ImportFormat.Csv
            : ImportFormat.Json;
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
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
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
    /// Enumerates objects from a JSON array or a sequence of top-level values without
    /// materializing the whole input.
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
    /// Detects a JSON array or a sequence of top-level objects, including a single object
    /// and JSON Lines. Empty input yields no items.
    /// </summary>
    /// <param name="stream">The seekable source stream.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Items with 1-based array indexes or physical start lines for object sequences.</returns>
    internal static async IAsyncEnumerable<(int LineNumber, JsonElement Item)> EnumerateJsonDocumentAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken token)
    {
        var (first, sourceEncoding) = await PeekFirstSignificantCharAsync(stream, token);
        if (first < 0)
        {
            yield break;
        }

        await using var input = Encoding.CreateTranscodingStream(
            stream,
            sourceEncoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Encoding.UTF8,
            leaveOpen: true);
        var source = first == '[' ? EnumerateArrayAsync(input, token) : EnumerateObjectsAsync(input, token);
        await using var items = source.GetAsyncEnumerator(token);
        while (await MoveNextDocumentAsync(items))
        {
            yield return items.Current;
        }
    }

    private static async IAsyncEnumerable<(int LineNumber, JsonElement Item)> EnumerateObjectsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        var offset = 0;
        var length = 0;
        var final = false;
        var line = 1;
        var previousCarriageReturn = false;
        ObjectBoundaryScanner scanner = default;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var whitespace = offset;
            while (whitespace < length && buffer[whitespace] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                whitespace++;
            }

            AdvanceLines(buffer.AsSpan(offset, whitespace - offset), ref line, ref previousCarriageReturn);
            offset = whitespace;
            if (offset < length)
            {
                if (buffer[offset] != (byte)'{')
                {
                    throw new CommandException(
                        "import",
                        MessageService.GetArgsString("command-import-error-not_object", "line", line));
                }

                if (TryReadObject(buffer.AsSpan(offset, length - offset), final, line, ref scanner, out var item, out var consumed))
                {
                    var startLine = line;
                    AdvanceLines(buffer.AsSpan(offset, consumed), ref line, ref previousCarriageReturn);
                    offset += consumed;
                    scanner = default;
                    yield return (startLine, item);
                    continue;
                }
            }
            else if (final)
            {
                yield break;
            }

            if (offset > 0)
            {
                buffer.AsSpan(offset, length - offset).CopyTo(buffer);
                length -= offset;
                offset = 0;
            }

            if (length == buffer.Length)
            {
                // Validate the pending object prefix before growing so malformed input fails
                // instead of being buffered without bound.
                ValidatePendingObject(buffer.AsSpan(0, length), line, ref scanner);
                Array.Resize(ref buffer, checked(buffer.Length * 2));
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length), token);
            length += read;
            final = read == 0;
        }
    }

    private static bool TryReadObject(ReadOnlySpan<byte> buffer, bool final, int line, ref ObjectBoundaryScanner scanner, out JsonElement item, out int consumed)
    {
        try
        {
            var complete = scanner.Scan(buffer);
            if (!complete && !final)
            {
                item = default;
                consumed = 0;
                return false;
            }

            var reader = new Utf8JsonReader(complete ? buffer[..scanner.BytesScanned] : buffer, isFinalBlock: true, default);
            if (JsonDocument.TryParseValue(ref reader, out var document))
            {
                using (document)
                {
                    item = document.RootElement.Clone();
                }

                consumed = checked((int)reader.BytesConsumed);
                return true;
            }
        }
        catch (JsonException ex)
        {
            throw new CommandException(
                "import",
                MessageService.GetArgsString("command-import-error-invalid_line_json", "line", line, "message", ex.Message),
                ex);
        }

        item = default;
        consumed = 0;
        return false;
    }

    private static void ValidatePendingObject(ReadOnlySpan<byte> buffer, int line, ref ObjectBoundaryScanner scanner)
    {
        try
        {
            scanner.ValidatePrefix(buffer);
        }
        catch (JsonException ex)
        {
            throw new CommandException(
                "import",
                MessageService.GetArgsString("command-import-error-invalid_line_json", "line", line, "message", ex.Message),
                ex);
        }
    }

    private static void AdvanceLines(ReadOnlySpan<byte> bytes, ref int line, ref bool previousCarriageReturn)
    {
        foreach (var value in bytes)
        {
            if (value == '\r' || (value == '\n' && !previousCarriageReturn))
            {
                line++;
            }

            previousCarriageReturn = value == '\r';
        }
    }

    private static async Task<bool> MoveNextDocumentAsync<T>(IAsyncEnumerator<T> enumerator)
    {
        try
        {
            return await enumerator.MoveNextAsync();
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            throw new CommandException(
                "import",
                MessageService.GetArgsString("command-import-error-invalid_document", "message", ex.Message),
                ex);
        }
    }

    private static async Task<(int First, Encoding? SourceEncoding)> PeekFirstSignificantCharAsync(Stream stream, CancellationToken token)
    {
        var start = stream.Position;
        var buffer = new byte[4096];
        var contentStart = start;
        try
        {
            // Only complete byte-order marks are recognized, even when split across reads;
            // partial prefixes fail strict decoding. Input is transcoded to validated UTF-8
            // by the caller.
            var read = await stream.ReadAtLeastAsync(buffer, 4, throwOnEndOfStream: false, token);
            var (sourceEncoding, preambleLength) = DetectByteOrderMark(buffer.AsSpan(0, read));
            contentStart = start + preambleLength;
            var encoding = sourceEncoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var decoder = encoding.GetDecoder();
            var chars = new char[encoding.GetMaxCharCount(buffer.Length)];
            var index = preambleLength;
            while (true)
            {
                var final = read == 0;
                var count = decoder.GetChars(buffer, index, read - index, chars, 0, flush: final);
                for (var i = 0; i < count; i++)
                {
                    if (chars[i] is not (' ' or '\t' or '\r' or '\n'))
                    {
                        return (chars[i], sourceEncoding);
                    }
                }

                if (final)
                {
                    return (-1, sourceEncoding);
                }

                read = await stream.ReadAsync(buffer, token);
                index = 0;
            }
        }
        catch (DecoderFallbackException ex)
        {
            throw new CommandException(
                "import",
                MessageService.GetArgsString("command-import-error-invalid_document", "message", ex.Message),
                ex);
        }
        finally
        {
            stream.Position = contentStart;
        }
    }

    private static (Encoding? Encoding, int PreambleLength) DetectByteOrderMark(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return (null, 3);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true), 4);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true), 4);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true), 2);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true), 2);
        }

        return (null, 0);
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

        var segments = SplitPartitionKeyPath(path.Trim());
        return segments.Count == 0 ? null : [.. segments];
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
        var records = ParseCsvWithLines(content, separator);
        var result = new List<List<string>>(records.Count);
        foreach (var record in records)
        {
            result.Add(record.Fields);
        }

        return result;
    }

    /// <summary>
    /// Parses CSV text like <see cref="ParseCsv"/> but also reports the 1-based physical
    /// line in the source text where each record begins. Quoted fields may span multiple
    /// physical lines, so the start line is tracked independently of the record index.
    /// </summary>
    /// <param name="content">The full CSV text.</param>
    /// <param name="separator">The field separator.</param>
    /// <returns>A list of (1-based start line, field values) records.</returns>
    internal static List<(int StartLine, List<string> Fields)> ParseCsvWithLines(string content, char separator)
    {
        using var reader = new StringReader(content);
        return ReadCsvRecords(reader, separator, CancellationToken.None).ToList();
    }

    internal static IEnumerable<(int StartLine, List<string> Fields)> ReadCsvRecords(TextReader reader, char separator, CancellationToken token)
    {
        using var parser = CreateCsvParser(reader, separator, token);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var startLine = parser.RawRow + 1;
            string[]? fields;
            try
            {
                if (!parser.Read())
                {
                    yield break;
                }

                fields = parser.Record;
            }
            catch (CsvHelperException ex)
            {
                throw new CommandException("import", MessageService.GetArgsString("command-import-error-invalid_csv", "line", startLine), ex);
            }

            if (fields is not null && parser.RawRecord.TrimEnd('\r', '\n').Length > 0)
            {
                yield return (startLine, fields.ToList());
            }
        }
    }

    internal static async IAsyncEnumerable<(int StartLine, List<string> Fields)> ReadCsvRecordsAsync(
        TextReader reader,
        char separator,
        [EnumeratorCancellation] CancellationToken token)
    {
        using var parser = CreateCsvParser(reader, separator, token);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var startLine = parser.RawRow + 1;
            string[]? fields;
            try
            {
                if (!await parser.ReadAsync())
                {
                    yield break;
                }

                fields = parser.Record;
            }
            catch (CsvHelperException ex)
            {
                throw new CommandException("import", MessageService.GetArgsString("command-import-error-invalid_csv", "line", startLine), ex);
            }

            if (fields is not null && parser.RawRecord.TrimEnd('\r', '\n').Length > 0)
            {
                yield return (startLine, fields.ToList());
            }
        }
    }

    // CsvParser.ReadAsync accepts no token, so cancellation is enforced at the reader to
    // interrupt a large or unterminated record while it is still being read.
    private static CsvParser CreateCsvParser(TextReader reader, char separator, CancellationToken token)
        => new(new CancellationAwareTextReader(reader, token), new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = separator.ToString(),
            IgnoreBlankLines = false,
            ExceptionMessagesContainRawData = false,
        });

    /// <summary>
    /// Builds a JSON object from a CSV header row and a value row. Every column becomes a
    /// top-level string property. When <paramref name="partitionKeySegments"/> describes a
    /// nested path, the matching leaf column is relocated under that nested path.
    /// </summary>
    /// <param name="headers">The header field names.</param>
    /// <param name="values">The row values.</param>
    /// <param name="partitionKeySegments">Optional partition key path segments.</param>
    /// <param name="lineNumber">The physical start line of the CSV record.</param>
    /// <returns>The constructed JSON object element.</returns>
    internal static JsonElement BuildCsvObject(IReadOnlyList<string> headers, IReadOnlyList<string> values, string[]? partitionKeySegments, int lineNumber)
    {
        var root = new JsonObject();
        for (var i = 0; i < headers.Count; i++)
        {
            var name = headers[i];
            if (string.IsNullOrEmpty(name))
            {
                if (i < values.Count && !string.IsNullOrEmpty(values[i]))
                {
                    throw new CommandException(
                        "import",
                        MessageService.GetArgsString(
                            "command-import-error-unnamed_csv_value",
                            "line",
                            lineNumber,
                            "column",
                            i + 1));
                }

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
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);
        List<string>? headers = null;
        await foreach (var (startLine, fields) in ReadCsvRecordsAsync(reader, ShellInterpreter.CSVSeparator, token))
        {
            if (headers is null)
            {
                headers = fields;
                continue;
            }

            yield return (startLine, BuildCsvObject(headers, fields, partitionKeySegments, startLine));
        }
    }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var selectedFormat = new CommandState();
        selectedFormat.SetFormat(this.Format);
        var concurrency = this.Concurrency ?? DefaultConcurrency;
        if (concurrency < 1)
        {
            throw new CommandException("import", MessageService.GetString("command-import-error-invalid_concurrency"));
        }

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
        IReadOnlyList<string> partitionKeyPaths = [];
        if (!dryRun)
        {
            if (shell.State is not ConnectedState connectedState)
            {
                throw new NotConnectedException("import");
            }

            (_, _, container, partitionKeyPaths) = await ResolveContainerWithPartitionKeyPathsAsync(
                connectedState,
                this.Database,
                this.Container,
                "import",
                token);
        }

        var format = ResolveFormat(filePath);
        var mode = this.Mode ?? ImportMode.Insert;
        var continueOnError = this.ContinueOnError == true;
        var partitionKeySegments = ParsePartitionKeySegments(this.PartitionKey);

        var progress = new ImportProgress();
        var reporter = new IngestionProgress(
            shell,
            selectedFormat,
            dryRun ? "command-import-validation-progress" : "command-import-progress");
        void UpdateProgress() => reporter.Update(
            dryRun ? progress.Validated : progress.Success + progress.Failed,
            progress.Success,
            progress.Failed,
            progress.Charge);
        Exception? failure = null;
        try
        {
            await reporter.RunAsync(() => ExecuteImportAsync(
                shell,
                filePath,
                format,
                mode,
                container,
                continueOnError,
                dryRun,
                partitionKeySegments,
                partitionKeyPaths,
                concurrency,
                commandState,
                progress,
                token,
                UpdateProgress));
        }
        catch (DecoderFallbackException ex)
        {
            failure = new CommandException(
                "import",
                MessageService.GetArgsString("command-import-error-invalid_document", "message", ex.Message),
                ex);
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            failure = ex;
        }
        catch (Exception ex) when (ex is CommandException or JsonException or IOException or UnauthorizedAccessException)
        {
            failure = ex;
        }

        failure ??= progress.FirstFailure;
        var (successCount, failCount, charge) = (progress.Success, progress.Failed, progress.Charge);

        string summary;
        if (dryRun)
        {
            summary = MessageService.GetArgsString(
                failure == null ? "command-import-dry-run-success" : "command-import-dry-run-stopped",
                "count",
                progress.Validated);
        }
        else if (failure != null && failCount == 0)
        {
            summary = MessageService.GetArgsString(
                "command-import-stopped",
                "success",
                successCount,
                "charge",
                charge.ToString("F2", CultureInfo.InvariantCulture));
        }
        else if (failCount == 0)
        {
            summary = MessageService.GetArgsString(
                "command-import-success",
                "count",
                successCount,
                "charge",
                charge.ToString("F2", CultureInfo.InvariantCulture));
        }
        else if (successCount > 0)
        {
            summary = MessageService.GetArgsString(
                "command-import-success-partial",
                "success",
                successCount,
                "failed",
                failCount,
                "charge",
                charge.ToString("F2", CultureInfo.InvariantCulture));
        }
        else
        {
            summary = MessageService.GetArgsString(
                "command-import-all-failed",
                "count",
                failCount);
        }

        var payload = JsonSerializer.SerializeToNode(new
        {
            type = "import",
            file = filePath,
            imported = successCount,
            failed = failCount,
            requestCharge = charge,
            dryRun,
        })!.AsObject();
        if (dryRun)
        {
            payload["validated"] = progress.Validated;
            payload["validationScope"] = "offline";
            payload["targetValidated"] = false;
        }

        CommandState resultState;
        if (failure != null)
        {
            payload["error"] = failure.Message;
            payload["exitCode"] = ShellExitCode.FromException(failure);
            resultState = new StructuredErrorCommandState(
                new CommandException("import", $"{summary} {failure.Message}", failure),
                new ShellJson(JsonSerializer.SerializeToElement(payload)));
        }
        else
        {
            selectedFormat.Result = new ShellJson(JsonSerializer.SerializeToElement(payload));
            resultState = selectedFormat;
        }

        if (selectedFormat.OutputFormatExplicitlySet)
        {
            resultState.OutputFormat = selectedFormat.OutputFormat;
        }

        resultState.ShareWarningsFrom(commandState);
        resultState.RequestCharge = charge > 0 ? charge : null;
        resultState.RenderUser = () =>
        {
            ShellInterpreter.WriteLine(summary);
            if (failure != null && shell.ErrOutRedirect == null)
            {
                ShellInterpreter.WriteLine(failure.Message);
            }
        };
        return resultState;
    }

    private static async Task ExecuteImportAsync(
        ShellInterpreter shell,
        string filePath,
        ImportFormat format,
        ImportMode mode,
        Container? container,
        bool continueOnError,
        bool dryRun,
        string[]? partitionKeySegments,
        IReadOnlyList<string> partitionKeyPaths,
        int concurrency,
        CommandState commandState,
        ImportProgress progress,
        CancellationToken token,
        Action progressChanged)
    {
        FileStream? stream = null;
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
                source = EnumerateJsonDocumentAsync(stream, token);
            }

            if (dryRun)
            {
                await foreach (var item in source.WithCancellation(token))
                {
                    ValidateDryRunItem(item.Item, item.LineNumber);
                    progress.Validated++;
                    progressChanged();
                    shell.ReportOperationProgress();
                }

                return;
            }

            ArgumentNullException.ThrowIfNull(container);
            await WriteItemsAsync(
                source,
                container,
                mode,
                concurrency,
                continueOnError,
                partitionKeyPaths,
                commandState,
                token,
                warningShell: shell,
                progress: progress,
                progressChanged: progressChanged);
        }
        finally
        {
            if (stream != null)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static void ValidateDryRunItem(JsonElement item, int lineNumber)
    {
        string? id = null;
        InvalidOperationException? decodingFailure = null;
        if (item.TryGetProperty("id", out var idElement)
            && idElement.ValueKind == JsonValueKind.String)
        {
            try
            {
                id = idElement.GetString();
            }
            catch (InvalidOperationException ex)
            {
                decodingFailure = ex;
            }
        }

        if (id is { Length: > 0 }
            && Encoding.UTF8.GetByteCount(id) <= MaxItemIdByteLength
            && !id.Contains('/')
            && !id.Contains('\\'))
        {
            return;
        }

        var message = MessageService.GetArgsString("command-import-error-invalid_id", "line", lineNumber);
        throw new CommandException("import", message, new ArgumentException(message, decodingFailure));
    }

    internal static async Task<(int Success, int Failed, double Charge)> WriteItemsAsync(
        IAsyncEnumerable<(int LineNumber, JsonElement Item)> source,
        Container container,
        ImportMode mode,
        int concurrency,
        bool continueOnError,
        CancellationToken token)
    {
        return await WriteItemsAsync(source, container, mode, concurrency, continueOnError, [], new CommandState(), token);
    }

    internal static async Task<(int Success, int Failed, double Charge)> WriteItemsAsync(
        IAsyncEnumerable<(int LineNumber, JsonElement Item)> source,
        Container container,
        ImportMode mode,
        int concurrency,
        bool continueOnError,
        IReadOnlyList<string> partitionKeyPaths,
        CommandState commandState,
        CancellationToken token,
        ShellInterpreter? warningShell = null,
        ImportProgress? progress = null,
        Action? progressChanged = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        progress ??= new ImportProgress();
        var pending = new List<Task<(double Charge, Exception? Error)>>();
        var undefinedPartitionKeyMutationCount = 0;
        CommandWarning? undefinedPartitionKeyWarning = null;

        void Record((double Charge, Exception? Error) result)
        {
            progress.Charge += result.Charge;
            if (result.Error is null)
            {
                progress.Success++;
            }
            else
            {
                progress.Failed++;
                progress.FirstFailure ??= result.Error;
            }

            progressChanged?.Invoke();
        }

        async Task DrainCompletedAsync()
        {
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].IsCompleted)
                {
                    Record(await pending[i]);
                    pending.RemoveAt(i);
                }
            }
        }

        await using var items = source.GetAsyncEnumerator(token);
        async ValueTask<bool> ReadNextAsync()
        {
            var next = items.MoveNextAsync();
            if (next.IsCompleted || pending.Count == 0)
            {
                return await next;
            }

            var read = next.AsTask();
            try
            {
                while (!read.IsCompleted && pending.Count > 0)
                {
                    await Task.WhenAny(pending.Cast<Task>().Append(read));
                    await DrainCompletedAsync();
                }

                return await read;
            }
            finally
            {
                // Settle the read before disposing its enumerator, even when a write fails.
                await read;
            }
        }

        try
        {
            while (true)
            {
                await DrainCompletedAsync();
                if (progress.Failed > 0 && !continueOnError)
                {
                    break;
                }

                if (pending.Count >= concurrency)
                {
                    await Task.WhenAny(pending).WaitAsync(token);
                    continue;
                }

                token.ThrowIfCancellationRequested();
                if (!await ReadNextAsync())
                {
                    break;
                }

                await DrainCompletedAsync();
                if (progress.Failed > 0 && !continueOnError)
                {
                    break;
                }

                token.ThrowIfCancellationRequested();
                if (items.Current.Item.ValueKind == JsonValueKind.Object
                    && HasUndefinedPartitionKeyComponent(items.Current.Item, partitionKeyPaths))
                {
                    if (undefinedPartitionKeyWarning is null)
                    {
                        undefinedPartitionKeyWarning = commandState.AddWarning(
                            MessageService.GetString("warning-undefined-partition-key-pending"));
                        warningShell?.RenderMutationWarnings(commandState);
                    }

                    undefinedPartitionKeyMutationCount++;
                    undefinedPartitionKeyWarning = AddUndefinedPartitionKeyWarning(
                        commandState,
                        undefinedPartitionKeyMutationCount,
                        undefinedPartitionKeyWarning);
                }

                pending.Add(WriteItemAsync(
                    container,
                    mode,
                    items.Current.LineNumber,
                    items.Current.Item,
                    partitionKeyPaths,
                    token,
                    warningShell));
            }
        }
        finally
        {
            try
            {
                // Drain every started write before returning or releasing the source, even on parse
                // errors. Record each write as it completes so progress does not wait for the slowest one.
                Task<(double Charge, Exception? Error)>? firstUnsuccessful = null;
                while (pending.Count > 0)
                {
                    var completed = await Task.WhenAny(pending);
                    pending.Remove(completed);
                    if (completed.IsCompletedSuccessfully)
                    {
                        Record(await completed);
                    }
                    else if (firstUnsuccessful is null || (firstUnsuccessful.IsCanceled && completed.IsFaulted))
                    {
                        firstUnsuccessful = completed;
                    }
                }

                if (firstUnsuccessful != null)
                {
                    // Matches Task.WhenAll: a fault takes precedence over cancellation.
                    await firstUnsuccessful;
                }
            }
            finally
            {
                if (undefinedPartitionKeyWarning != null)
                {
                    warningShell?.RenderUpdatedWarnings(commandState);
                }
            }
        }

        token.ThrowIfCancellationRequested();
        return (progress.Success, progress.Failed, progress.Charge);
    }

    private static async Task<(double Charge, Exception? Error)> WriteItemAsync(
        Container container,
        ImportMode mode,
        int lineNumber,
        JsonElement item,
        IReadOnlyList<string> partitionKeyPaths,
        CancellationToken token,
        ShellInterpreter? shell)
    {
        try
        {
            var options = new ItemRequestOptions { EnableContentResponseOnWrite = false };
            if (partitionKeyPaths.Count == 0)
            {
                var itemResponse = mode == ImportMode.Upsert
                    ? await container.UpsertItemAsync(item, requestOptions: options, cancellationToken: token)
                    : await container.CreateItemAsync(item, requestOptions: options, cancellationToken: token);
                var itemOk = itemResponse.StatusCode == System.Net.HttpStatusCode.Created ||
                    (mode == ImportMode.Upsert && itemResponse.StatusCode == System.Net.HttpStatusCode.OK);
                var itemFailure = itemOk
                    ? null
                    : new CommandException(
                        "import",
                        MessageService.GetArgsString(
                            "command-import-error-item_status",
                            "line",
                            lineNumber,
                            "status",
                            itemResponse.StatusCode.ToString()),
                        new global::Azure.RequestFailedException((int)itemResponse.StatusCode, itemResponse.StatusCode.ToString()));
                return (itemResponse.RequestCharge, itemFailure);
            }

            using var body = new MemoryStream(Encoding.UTF8.GetBytes(item.GetRawText()));
            PartitionKey partitionKey;
            try
            {
                TryCreatePartitionKeyFromItem(item, partitionKeyPaths, out _, out partitionKey);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                shell?.ReportOperationProgress();
                return (0, new CommandException(
                    "import",
                    MessageService.GetArgsString(
                        "command-import-error-item_failed",
                        "line",
                        lineNumber,
                        "status",
                        "Invalid partition key",
                        "message",
                        ex.Message),
                    ex is JsonException ? ex : new JsonException(ex.Message, ex)));
            }

            using var response = mode == ImportMode.Upsert
                ? await container.UpsertItemStreamAsync(body, partitionKey, options, token)
                : await container.CreateItemStreamAsync(body, partitionKey, options, token);
            response.EnsureSuccessStatusCode();
            var ok = response.StatusCode == System.Net.HttpStatusCode.Created ||
                (mode == ImportMode.Upsert && response.StatusCode == System.Net.HttpStatusCode.OK);
            var failure = ok
                ? null
                : new CommandException(
                    "import",
                    MessageService.GetArgsString(
                        "command-import-error-item_status",
                        "line",
                        lineNumber,
                        "status",
                        response.StatusCode.ToString()),
                    new global::Azure.RequestFailedException((int)response.StatusCode, response.StatusCode.ToString()));
            return (response.Headers.RequestCharge, failure);
        }
        catch (CosmosException ce)
        {
            return (ce.RequestCharge, new CommandException(
                "import",
                MessageService.GetArgsString(
                    "command-import-error-item_failed",
                    "line",
                    lineNumber,
                    "status",
                    ce.StatusCode.ToString(),
                    "message",
                    CommandException.GetDisplayMessage(ce)),
                ce));
        }
        catch (Exception ex) when (!token.IsCancellationRequested && CommandException.IsConnectivityFailure(ex))
        {
            // SDK request timeouts can surface as OperationCanceledException while the caller's
            // token is still active; record them per item so settled writes are not lost.
            return (0, new CommandException(
                "import",
                MessageService.GetArgsString(
                    "command-import-error-item_failed",
                    "line",
                    lineNumber,
                    "status",
                    ex is OperationCanceledException ? System.Net.HttpStatusCode.RequestTimeout.ToString() : ex.GetType().Name,
                    "message",
                    CommandException.GetDisplayMessage(ex)),
                ex));
        }
    }

    internal struct ObjectBoundaryScanner
    {
        private int depth;
        private bool inString;
        private bool escaped;
        private JsonReaderState validationState;

        internal int BytesScanned { get; private set; }

        internal int BytesValidated { get; private set; }

        internal void ValidatePrefix(ReadOnlySpan<byte> buffer)
        {
            var reader = new Utf8JsonReader(buffer[this.BytesValidated..], isFinalBlock: false, this.validationState);
            while (reader.Read())
            {
                if (reader.CurrentDepth == 0 && reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }
            }

            this.BytesValidated += checked((int)reader.BytesConsumed);
            this.validationState = reader.CurrentState;
        }

        internal bool Scan(ReadOnlySpan<byte> buffer)
        {
            // Retain framing progress across refills; Utf8JsonReader validates the complete object.
            while (this.BytesScanned < buffer.Length)
            {
                var value = buffer[this.BytesScanned++];
                if (this.inString)
                {
                    if (this.escaped)
                    {
                        this.escaped = false;
                    }
                    else if (value == '\\')
                    {
                        this.escaped = true;
                    }
                    else if (value == '"')
                    {
                        this.inString = false;
                    }
                }
                else if (value == '"')
                {
                    this.inString = true;
                }
                else if (value is (byte)'{' or (byte)'[')
                {
                    this.depth++;
                    if (this.depth > 64)
                    {
                        throw new JsonException("The maximum JSON depth of 64 has been exceeded.");
                    }
                }
                else if (value is (byte)'}' or (byte)']')
                {
                    this.depth--;
                    if (this.depth == 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    internal sealed class ImportProgress
    {
        internal int Validated { get; set; }

        internal int Success { get; set; }

        internal int Failed { get; set; }

        internal double Charge { get; set; }

        internal Exception? FirstFailure { get; set; }
    }

    private sealed class CancellationAwareTextReader(TextReader inner, CancellationToken token) : TextReader
    {
        public override int Peek()
        {
            token.ThrowIfCancellationRequested();
            return inner.Peek();
        }

        public override int Read()
        {
            token.ThrowIfCancellationRequested();
            return inner.Read();
        }

        public override int Read(char[] buffer, int index, int count)
        {
            token.ThrowIfCancellationRequested();
            return inner.Read(buffer, index, count);
        }

        public override int Read(Span<char> buffer)
        {
            token.ThrowIfCancellationRequested();
            return inner.Read(buffer);
        }

        public override Task<int> ReadAsync(char[] buffer, int index, int count)
            => this.ReadAsync(buffer.AsMemory(index, count), CancellationToken.None).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            token.ThrowIfCancellationRequested();
            if (!cancellationToken.CanBeCanceled)
            {
                return await inner.ReadAsync(buffer, token).ConfigureAwait(false);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            return await inner.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
