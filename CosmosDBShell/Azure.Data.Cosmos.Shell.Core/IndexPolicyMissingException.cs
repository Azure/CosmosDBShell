// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Thrown by <see cref="CosmosResourceFacade.GetIndexingPolicyJsonAsync"/> when a
/// container has no indexing policy attached. Derives from
/// <see cref="System.InvalidOperationException"/> so existing callers that catch
/// the broader type continue to work; new callers can match this specific type to
/// translate to a localized command error.
/// </summary>
internal sealed class IndexPolicyMissingException : System.InvalidOperationException
{
    public IndexPolicyMissingException()
        : base("Container has no indexing policy.")
    {
    }
}
