//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.IO;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("export")]
[CosmosExample("export items.json", DescriptionKey = "command-export-example-1")]
[CosmosExample("export items.json --query=\"SELECT * FROM c WHERE c.status = 'active'\"", DescriptionKey = "command-export-example-2")]
[CosmosExample("export items.json --force", DescriptionKey = "command-export-example-3")]
[CosmosExample("export items.json --format=csv", DescriptionKey = "command-export-example-4")]
[CosmosExample("export items.json --db=MyDB --con=Products --max=1000", DescriptionKey = "command-export-example-5")]
[McpAnnotation(
    Title = "Export Container Items",
    ReadOnly = false,
    Idempotent = false,
    OpenWorld = true,
    Description = "Streams items from a Cosmos container into a local JSON array file. The format option controls the command result, not the exported file.")]
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
        var (count, charge) = await ExecuteExportAsync(container, query, max, filePath, this.Force == true, token);

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

    private static async Task<(int Count, double Charge)> ExecuteExportAsync(
        Container container,
        string query,
        int? max,
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
                EnumerateAsync(iterator, max, charge => totalCharge += charge, token), filePath, overwrite, token);
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
        string filePath,
        bool overwrite,
        CancellationToken token)
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
                count = await WriteArrayAsync(items, stream, token);
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
