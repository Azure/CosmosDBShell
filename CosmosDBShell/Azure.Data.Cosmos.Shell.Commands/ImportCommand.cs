//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

internal enum ImportFormat
{
    Auto = 0,
    JsonLines = 1,
    Array = 2,
}

internal enum ImportMode
{
    Insert = 0,
    Upsert = 1,
}

[CosmosCommand("import")]
[CosmosExample("import items.jsonl", Description = "Import items from a JSON Lines file (one JSON object per line)")]
[CosmosExample("import items.json --format=array", Description = "Import items from a JSON array file")]
[CosmosExample("import items.jsonl --mode=upsert", Description = "Insert new items and replace any existing items with the same id")]
[CosmosExample("import items.jsonl --continue-on-error", Description = "Keep importing after individual item failures")]
[CosmosExample("import items.jsonl --dry-run", Description = "Validate the file without writing any items")]
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

        var (successCount, failCount, charge) = await ExecuteImportAsync(filePath, format, mode, container, continueOnError, dryRun, token);

        if (dryRun)
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-import-dry-run-success",
                "count",
                successCount,
                "failed",
                failCount));
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
        CancellationToken token)
    {
        var success = 0;
        var failed = 0;
        var charge = 0.0;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        IAsyncEnumerable<(int LineNumber, JsonElement Item)> source;
        StreamReader? lineReader = null;
        try
        {
            if (format == ImportFormat.Array)
            {
                source = EnumerateArrayAsync(stream, token);
            }
            else
            {
                lineReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                source = EnumerateJsonLinesAsync(lineReader, token);
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
        }

        return (success, failed, charge);
    }
}
