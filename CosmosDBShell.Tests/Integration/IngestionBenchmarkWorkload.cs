// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// The shared ingestion benchmark workload: 1 KiB JSON items spread evenly across 1,000
/// partition-key values, and the CSV row format for results.
/// </summary>
internal static class IngestionBenchmarkWorkload
{
    internal const int ItemBytes = 1024;

    internal const string CsvHeader = "command,items,itemBytes,inputBytes,concurrency,elapsedSeconds,itemsPerSecond,requestCharge,verifiedItems";

    internal static readonly int[] Sizes = [1_000, 10_000, 100_000];

    internal static readonly string[] Commands = ["import", "bulk"];

    internal static readonly int[] Concurrencies = [1, 16, 32];

    /// <summary>
    /// Writes a JSON Lines input file. When <paramref name="caseId"/> is set, IDs are prefixed with
    /// it and items carry a <c>caseId</c> property, so several cases can share one container.
    /// </summary>
    internal static async Task WriteInputAsync(string path, int size, bool bulk, string? caseId, CancellationToken token)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\n" };
        string? payload = null;
        for (var index = 0; index < size; index++)
        {
            token.ThrowIfCancellationRequested();
            var id = caseId is null
                ? index.ToString("D8", CultureInfo.InvariantCulture)
                : $"{caseId}-{index.ToString("D8", CultureInfo.InvariantCulture)}";
            var pk = $"p{index % 1000:D4}";
            payload ??= new string('x', ItemBytes - Encoding.UTF8.GetByteCount(Serialize(id, pk, caseId, string.Empty)));
            var item = Serialize(id, pk, caseId, payload);
            Assert.Equal(ItemBytes, Encoding.UTF8.GetByteCount(item));
            await writer.WriteLineAsync(bulk ? $$"""{"op":"create","item":{{item}}}""" : item);
        }
    }

    internal static string FormatRow(string command, int size, long inputBytes, int concurrency, TimeSpan elapsed, JsonElement result, long verified) =>
        string.Join(
            ",",
            command,
            size,
            ItemBytes,
            inputBytes,
            concurrency,
            elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
            (size / elapsed.TotalSeconds).ToString("F1", CultureInfo.InvariantCulture),
            result.GetProperty("requestCharge").GetDouble().ToString("F2", CultureInfo.InvariantCulture),
            verified);

    private static string Serialize(string id, string pk, string? caseId, string payload) => caseId is null
        ? JsonSerializer.Serialize(new { id, pk, payload })
        : JsonSerializer.Serialize(new { id, pk, caseId, payload });
}
