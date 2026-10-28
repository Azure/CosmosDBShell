// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal sealed record ResourceCreationResult(string Id, bool? Created)
{
    // ARM create-or-update responses do not distinguish creation from update.
    public string Outcome => this.Created switch
    {
        true => "created",
        false => "existing",
        null => "createdOrUpdated",
    };
}
