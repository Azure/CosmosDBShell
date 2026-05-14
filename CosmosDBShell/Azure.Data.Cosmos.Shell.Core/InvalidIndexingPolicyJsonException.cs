// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Thrown by <see cref="CosmosResourceFacade"/> when an indexing policy JSON
/// payload supplied by the user cannot be deserialized. Derives from
/// <see cref="System.InvalidOperationException"/> so existing callers that catch
/// the broader type continue to work, while new callers can match this specific
/// type to translate to a localized command error.
/// </summary>
internal sealed class InvalidIndexingPolicyJsonException : System.InvalidOperationException
{
    public InvalidIndexingPolicyJsonException()
        : base("Unable to parse indexing policy JSON.")
    {
    }

    public InvalidIndexingPolicyJsonException(System.Exception innerException)
        : base("Unable to parse indexing policy JSON.", innerException)
    {
    }
}
