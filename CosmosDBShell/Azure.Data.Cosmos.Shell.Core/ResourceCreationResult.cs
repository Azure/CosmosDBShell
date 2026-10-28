// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Diagnostics;
using System.Text.Json;

internal sealed record ResourceCreationResult(string Id, ResourceCreationOutcome Outcome)
{
    public bool? Created => this.Outcome switch
    {
        ResourceCreationOutcome.Created => true,
        ResourceCreationOutcome.Existing => false,
        ResourceCreationOutcome.CreatedOrUpdated => null,
        _ => throw new UnreachableException(),
    };

    public string OutcomeName => JsonNamingPolicy.CamelCase.ConvertName(this.Outcome.ToString());
}
