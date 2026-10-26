// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// A durable, append-only JSON Lines log of write intents and outcomes for one operation list.
/// The intent is recorded before each write is sent, so a resumed run never mistakes an
/// interrupted write for one that was not attempted.
/// </summary>
internal sealed class BulkJournal : IDisposable
{
    private const int Version = 2;

    private readonly FileStream stream;

    private ExceptionDispatchInfo? appendFailure;

    internal BulkJournal(FileStream stream, Dictionary<long, BulkOutcome> previous)
    {
        this.stream = stream;
        this.Previous = previous;
    }

    public IReadOnlyDictionary<long, BulkOutcome> Previous { get; }

    public static BulkJournal Open(string path, string endpoint, string containerRid, string hash, long operationCount)
    {
        var fullPath = Path.GetFullPath(path);
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        FileStream stream;
        try
        {
            stream = new FileStream(fullPath, options);
        }
        catch (IOException ex)
        {
            throw new CommandException("bulk", MessageService.GetArgsString("command-bulk-error-journal_open", "file", fullPath, "message", ex.Message), ex);
        }

        try
        {
            var header = new Header(Version, endpoint, containerRid, hash, operationCount);
            var previous = new Dictionary<long, BulkOutcome>();
            if (stream.Length == 0)
            {
                Append(stream, JsonSerializer.Serialize(header, BulkCommand.JsonOptions));
            }
            else
            {
                var completeLength = FindCompleteLength(stream);
                if (completeLength == 0)
                {
                    throw Invalid(null);
                }

                ReadExisting(stream, header, previous, completeLength < stream.Length);
                stream.SetLength(completeLength);
                stream.Seek(0, SeekOrigin.End);
            }

            return new BulkJournal(stream, previous);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public async Task RecordAsync(BulkOutcome outcome)
    {
        this.appendFailure?.Throw();
        var entry = new Entry(outcome, ComputeChecksum(outcome));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, BulkCommand.JsonOptions) + "\n");
        try
        {
            await this.stream.WriteAsync(bytes);
            await this.stream.FlushAsync();
        }
        catch (Exception ex)
        {
            // No later record may turn a torn tail into an invalid complete line.
            this.appendFailure = ExceptionDispatchInfo.Capture(ex);
            throw;
        }
    }

    public async Task ValidateAsync(IAsyncEnumerable<BulkOperation> operations, CancellationToken token)
    {
        if (this.Previous.Count == 0)
        {
            return;
        }

        var validated = 0;
        await foreach (var operation in operations.WithCancellation(token))
        {
            if (this.Previous.TryGetValue(operation.Index, out var outcome))
            {
                if (outcome.Op != operation.Op || outcome.Id != operation.Id ||
                    outcome.PartitionKey.GetRawText() != operation.PartitionKey)
                {
                    throw Invalid(null);
                }

                validated++;
            }
        }

        if (validated != this.Previous.Count)
        {
            throw Invalid(null);
        }
    }

    public void Dispose() => this.stream.Dispose();

    private static void Append(FileStream stream, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        stream.Write(bytes);
        stream.Flush();
    }

    // A crash can leave a partially written final line. Its write had not been sent (intent) or
    // its outcome is unknown (the earlier intent remains), so dropping it never hides an attempt.
    private static long FindCompleteLength(FileStream stream)
    {
        var length = stream.Length;
        if (length == 0)
        {
            return 0;
        }

        var buffer = new byte[4096];
        var end = length;
        while (end > 0)
        {
            var start = Math.Max(0, end - buffer.Length);
            stream.Position = start;
            stream.ReadExactly(buffer, 0, (int)(end - start));
            for (var i = (int)(end - start) - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n')
                {
                    return start + i + 1;
                }
            }

            end = start;
        }

        return 0;
    }

    private static void ReadExisting(FileStream stream, Header expected, Dictionary<long, BulkOutcome> previous, bool tornTail)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 65536, leaveOpen: true);
        try
        {
            var header = JsonSerializer.Deserialize<Header>(reader.ReadLine() ?? string.Empty, BulkCommand.JsonOptions);
            if (header != expected)
            {
                throw new CommandException("bulk", MessageService.GetString("command-bulk-error-journal_mismatch"));
            }

            while (reader.ReadLine() is { } line)
            {
                if (tornTail && reader.EndOfStream)
                {
                    break;
                }

                var entry = JsonSerializer.Deserialize<Entry>(line, BulkCommand.JsonOptions);
                if (entry is not { Outcome: { } outcome } || outcome.Index < 0 || outcome.Index >= expected.OperationCount ||
                    !BulkOutcome.IsKnownStatus(outcome.Status) || outcome.PartitionKey.ValueKind != JsonValueKind.Array ||
                    entry.Checksum != ComputeChecksum(outcome))
                {
                    throw Invalid(null);
                }

                previous[outcome.Index] = outcome;
            }
        }
        catch (JsonException ex)
        {
            throw Invalid(ex);
        }
    }

    private static CommandException Invalid(Exception? inner) => new("bulk", MessageService.GetString("command-bulk-error-journal_invalid"), inner);

    private static string ComputeChecksum(BulkOutcome outcome) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(outcome, BulkCommand.JsonOptions)));

    private sealed record Entry(BulkOutcome Outcome, string Checksum);

    private sealed record Header(int Version, string Endpoint, string ContainerRid, string Hash, long OperationCount);
}
