//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Text.Json;

internal enum BatchOperationKind
{
    Create,
    Upsert,
    Replace,
    Delete,
    Patch,
}

internal sealed class BatchOperationSpec
{
    public BatchOperationKind Kind { get; init; }

    public string? Id { get; init; }

    public JsonElement? Item { get; init; }

    public IReadOnlyList<PatchOperation>? PatchOperations { get; init; }
}
