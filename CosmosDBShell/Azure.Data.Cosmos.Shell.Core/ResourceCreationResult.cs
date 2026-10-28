// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal sealed record ResourceCreationResult(string Id, bool? Created)
{
    // ARM create-or-update responses do not distinguish creation from update.
    public string Outcome => this.Select("created", "existing", "createdOrUpdated");

    public T Select<T>(T created, T existing, T createdOrUpdated) =>
        this.Created.HasValue
            ? (this.Created.Value ? created : existing)
            : createdOrUpdated;
}
