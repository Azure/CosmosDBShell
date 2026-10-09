// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// A private, self-deleting temporary file holding validated operations, so large jobs are
/// validated completely before any write without retaining all operations in memory.
/// </summary>
internal sealed class BulkSpool : IDisposable
{
    private static readonly byte[] NewLine = "\n"u8.ToArray();

    private readonly FileStream stream;
    private readonly StreamWriter writer;
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    private BulkSpool(FileStream stream)
    {
        this.stream = stream;
        this.writer = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true) { NewLine = "\n" };
    }

    public long Count { get; private set; }

    public static BulkSpool Create()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new BulkSpool(new FileStream(Path.Combine(Path.GetTempPath(), $"cosmos-bulk-{Guid.NewGuid():N}.jsonl"), options));
    }

    public static string ComputeHash(IEnumerable<string> lines)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var line in lines)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(line));
            hash.AppendData(NewLine);
        }

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    public async Task AddAsync(string json)
    {
        await this.writer.WriteLineAsync(json);
        this.hash.AppendData(Encoding.UTF8.GetBytes(json));
        this.hash.AppendData(NewLine);
        this.Count++;
    }

    public string GetHash() => Convert.ToHexString(this.hash.GetCurrentHash());

    public async IAsyncEnumerable<BulkOperation> ReadAsync([EnumeratorCancellation] CancellationToken token)
    {
        await this.writer.FlushAsync(token);
        this.stream.Position = 0;
        using var reader = new StreamReader(this.stream, new UTF8Encoding(false), false, 65536, leaveOpen: true);
        for (long index = 0; index < this.Count; index++)
        {
            var line = await reader.ReadLineAsync(token) ?? throw new InvalidDataException("The bulk spool ended unexpectedly.");
            yield return BulkOperation.FromJson(line, index);
        }
    }

    public void Dispose()
    {
        this.writer.Dispose();
        this.stream.Dispose();
        this.hash.Dispose();
    }
}
