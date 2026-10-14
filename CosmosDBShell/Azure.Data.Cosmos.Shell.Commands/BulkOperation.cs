// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;

/// <summary>
/// A validated bulk operation in canonical JSON form with its complete partition key.
/// </summary>
/// <param name="Index">Zero-based position in the operation list.</param>
/// <param name="Op">The operation kind: create, upsert, replace, delete, or patch.</param>
/// <param name="Id">The target item ID.</param>
/// <param name="PartitionKey">The canonical partition key as a JSON array.</param>
/// <param name="Json">The canonical operation JSON.</param>
internal sealed record BulkOperation(long Index, string Op, string Id, string PartitionKey, string Json)
{
    public static BulkOperation FromJson(string json, long index)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new BulkOperation(
            index,
            root.GetProperty("op").GetString()!,
            root.GetProperty("id").GetString()!,
            root.GetProperty("partitionKey").GetRawText(),
            json);
    }
}
