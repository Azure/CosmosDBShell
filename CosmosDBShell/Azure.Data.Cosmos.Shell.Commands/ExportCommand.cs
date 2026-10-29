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
    Json,
    Csv,
}

[CosmosCommand("export")]
[CosmosExample("export items.json", DescriptionKey = "command-export-example-1")]
[CosmosExample("export items.json --query=\"SELECT * FROM c WHERE c.status = 'active'\"", DescriptionKey = "command-export-example-2")]
[CosmosExample("export items.json --force", DescriptionKey = "command-export-example-3")]
[CosmosExample("export items.csv", DescriptionKey = "command-export-example-4")]
[CosmosExample("export items.json --db=MyDB --con=Products --max=1000", DescriptionKey = "command-export-example-5")]
[McpAnnotation(
    Title = "Export Container Items",
    ReadOnly = false,
    Idempotent = false,
    OpenWorld = true,
    Description = "Streams items from a Cosmos container into a local file: CSV for a .csv file name, otherwise a JSON array. The format option controls the command result, not the exported file.")]
internal class ExportCommand : CosmosCommand
{
    private const string DefaultQuery = "SELECT * FROM c";
    private const string ScalarColumnEnvironmentVariable = "COSMOSDB_SHELL_CSV_SCALAR_COLUMN";

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

    [CosmosOption("format", "f")]
    public string? Format { get; init; }

    [CosmosOption("force")]
    public bool? Force { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var resultState = new CommandState();
        resultState.SetFormat(this.Format);
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
        var format = ResolveFormat(filePath);

        var (count, charge) = await ExecuteExportAsync(shell, container, query, max, format, filePath, this.Force == true, token);

        resultState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            type = "export",
            file = filePath,
            exported = count,
            requestCharge = charge,
        }));
        resultState.RequestCharge = charge;
        resultState.RenderUser = () => ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-export-success",
            "count",
            count,
            "file",
            filePath,
            "charge",
            charge.ToString("F2", CultureInfo.InvariantCulture)));
        return resultState;
    }

    /// <summary>
    /// Resolves the exported file format from the destination file name: CSV for a
    /// <c>.csv</c> extension, otherwise a JSON array.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    /// <returns>The file format to write.</returns>
    internal static ExportFormat ResolveFormat(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase)
            ? ExportFormat.Csv
            : ExportFormat.Json;

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
    /// Writes a sequence of items to <paramref name="stream"/> as a single indented JSON array,
    /// the document shape the Azure Databases VS Code extension imports and exports, and
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
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 4, NewLine = "\n" });
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
    /// contains the corresponding values. Non-object rows are written in a configurable
    /// column with an empty header by default. Nested objects and arrays are written as
    /// compact JSON. Items are spooled to disk to compute the column set.
    /// </summary>
    /// <param name="items">The items to write.</param>
    /// <param name="writer">The destination writer.</param>
    /// <param name="separator">The field separator.</param>
    /// <param name="token">Cancellation token.</param>
    /// <param name="scalarColumnName">The header for non-object rows; defaults to the environment setting.</param>
    /// <param name="onRowWritten">Invoked after each row of the final pass, which runs after the last query page.</param>
    /// <returns>The number of data rows written.</returns>
    internal static async Task<int> WriteCsvAsync(IAsyncEnumerable<JsonElement> items, TextWriter writer, char separator, CancellationToken token, string? scalarColumnName = null, Action? onRowWritten = null)
    {
        scalarColumnName ??= Environment.GetEnvironmentVariable(ScalarColumnEnvironmentVariable) ?? string.Empty;
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
        var objectHeaders = new HashSet<string>(StringComparer.Ordinal);
        var hasScalarRows = false;
        await foreach (var item in items.WithCancellation(token))
        {
            await spoolWriter.WriteLineAsync(SerializeJsonLine(item).AsMemory(), token);
            if (item.ValueKind != JsonValueKind.Object)
            {
                hasScalarRows = true;
                if (headerSet.Add(scalarColumnName))
                {
                    headers.Add(scalarColumnName);
                }

                continue;
            }

            foreach (var prop in item.EnumerateObject())
            {
                objectHeaders.Add(prop.Name);
                if (headerSet.Add(prop.Name))
                {
                    headers.Add(prop.Name);
                }
            }
        }

        if (hasScalarRows && objectHeaders.Contains(scalarColumnName))
        {
            throw new CommandException(
                "export",
                MessageService.GetArgsString("command-export-error-scalar_column_conflict", "column", scalarColumnName));
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
                else if (item.ValueKind != JsonValueKind.Object && headers[i] == scalarColumnName)
                {
                    sb.Append(CommandState.EscapeCSV(GetScalarCsvValue(item)));
                }
                else
                {
                    sb.Append(CommandState.EscapeCSV(string.Empty));
                }
            }

            await writer.WriteLineAsync(sb.ToString().AsMemory(), token);
            count++;
            onRowWritten?.Invoke();
        }

        await writer.FlushAsync(token);
        return count;
    }

    private static string GetScalarCsvValue(JsonElement item)
    {
        return item.ToString();
    }

    private static async Task<(int Count, double Charge)> ExecuteExportAsync(
        ShellInterpreter shell,
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
                EnumerateAsync(
                    iterator,
                    max,
                    charge =>
                    {
                        totalCharge += charge;
                        shell.ReportOperationProgress();
                    },
                    token),
                format,
                filePath,
                overwrite,
                token,
                shell.ReportOperationProgress);
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
        CancellationToken token,
        Action? onRowWritten = null)
    {
        var destination = Path.GetFullPath(filePath);

        // A directory destination can never be overwritten by File.Move, so reject it before spending RU on the query.
        if (Directory.Exists(destination))
        {
            throw new IOException(MessageService.GetArgsString("command-export-error-destination_directory", "file", destination));
        }

        if (!overwrite && System.IO.File.Exists(destination))
        {
            throw new IOException(MessageService.GetArgsString("command-export-error-file_exists", "file", destination));
        }

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
                if (format == ExportFormat.Csv)
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.NewLine = "\n";
                    count = await WriteCsvAsync(items, writer, ShellInterpreter.CSVSeparator, token, onRowWritten: onRowWritten);
                }
                else
                {
                    count = await WriteArrayAsync(items, stream, token);
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
