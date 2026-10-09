// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Commands;

internal sealed class PendingBulkState
{
    public PendingBulkState(
        string databaseName,
        string containerName,
        string containerRid,
        IReadOnlyList<string> partitionKeyPaths,
        string? partitionKeyArgument,
        string? defaultPartitionKey)
    {
        this.DatabaseName = databaseName;
        this.ContainerName = containerName;
        this.ContainerRid = containerRid;
        this.PartitionKeyPaths = partitionKeyPaths;
        this.PartitionKeyArgument = partitionKeyArgument;
        this.DefaultPartitionKey = defaultPartitionKey;
    }

    public string DatabaseName { get; }

    public string ContainerName { get; }

    public string ContainerRid { get; }

    public IReadOnlyList<string> PartitionKeyPaths { get; }

    public string? PartitionKeyArgument { get; }

    public string? DefaultPartitionKey { get; }

    public List<BulkOperation> Operations { get; } = [];

    // Outcomes from earlier 'bulk execute' attempts, so a retry never replays a succeeded write.
    public Dictionary<long, BulkOutcome> Outcomes { get; } = [];
}
