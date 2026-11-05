// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

internal sealed class PendingBulkState
{
    internal const int MaxOperations = 10_000;

    internal const long MaxCanonicalJsonBytes = 64 * 1024 * 1024;

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

    public long CanonicalJsonBytes { get; set; }

    public string? JournalPath { get; set; }

    // Outcomes from earlier 'bulk execute' attempts, so a retry never replays a succeeded write.
    public Dictionary<long, BulkOutcome> Outcomes { get; } = [];

    internal static void ValidateCapacity(int operationCount, long canonicalJsonBytes)
    {
        if (operationCount > MaxOperations || canonicalJsonBytes > MaxCanonicalJsonBytes)
        {
            throw new CommandException("bulk", MessageService.GetString("command-bulk-error-stateful_limit"));
        }
    }
}
