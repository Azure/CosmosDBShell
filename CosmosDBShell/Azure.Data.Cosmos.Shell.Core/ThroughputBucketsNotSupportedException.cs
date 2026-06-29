// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Thrown when a throughput bucket configuration operation is attempted on a connection
/// that is not backed by Azure Resource Manager. Per-bucket throughput limits are a
/// control-plane setting and are only available when connected with an Entra (Azure AD)
/// credential. Callers can match this type to translate to a localized command error.
/// </summary>
internal sealed class ThroughputBucketsNotSupportedException : System.InvalidOperationException
{
    public ThroughputBucketsNotSupportedException(System.Exception? innerException = null)
        : base("Throughput bucket configuration requires an Azure AD (Entra) connection.", innerException)
    {
    }
}
