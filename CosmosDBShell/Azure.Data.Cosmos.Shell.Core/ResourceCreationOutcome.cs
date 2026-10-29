// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal enum ResourceCreationOutcome
{
    Created,
    Existing,

    // ARM create-or-update responses do not distinguish creation from update.
    CreatedOrUpdated,
}
