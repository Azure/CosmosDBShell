//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal sealed class PendingBatchState
{
    public PendingBatchState(string databaseName, string containerName, string partitionKeyArgument, PartitionKey partitionKey)
    {
        this.DatabaseName = databaseName;
        this.ContainerName = containerName;
        this.PartitionKeyArgument = partitionKeyArgument;
        this.PartitionKey = partitionKey;
    }

    public string DatabaseName { get; }

    public string ContainerName { get; }

    public string PartitionKeyArgument { get; }

    public PartitionKey PartitionKey { get; }

    public List<BatchOperationSpec> Operations { get; } = [];
}
