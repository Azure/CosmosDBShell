// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;

/// <summary>
/// A per-operation journal record: either the intent to write ("started") or its outcome.
/// </summary>
internal sealed record BulkOutcome(
    long Index,
    string Op,
    string Id,
    JsonElement PartitionKey,
    string Status,
    double RequestCharge = 0,
    int? StatusCode = null,
    string? Error = null)
{
    public const string Started = "started";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";

    public const string Uncertain = "uncertain";

    public static bool IsKnownStatus(string? status) => status is Started or Succeeded or Failed or Uncertain;

    public static BulkOutcome Create(BulkOperation operation, string status, double requestCharge = 0, int? statusCode = null, string? error = null)
    {
        return new BulkOutcome(
            operation.Index,
            operation.Op,
            operation.Id,
            JsonSerializer.Deserialize<JsonElement>(operation.PartitionKey),
            status,
            requestCharge,
            statusCode,
            error);
    }
}
