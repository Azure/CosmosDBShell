// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Thrown when a throughput operation targets a resource that has no provisioned
/// throughput (for example a container inside a shared-throughput database, or a
/// serverless account). New callers can match this specific type to translate to a
/// localized command error.
/// </summary>
internal sealed class ThroughputNotConfiguredException : System.InvalidOperationException
{
    public ThroughputNotConfiguredException(string resourceName, System.Exception? innerException = null)
        : base($"Resource '{resourceName}' has no provisioned throughput.", innerException)
    {
        this.ResourceName = resourceName;
    }

    public string ResourceName { get; }
}
