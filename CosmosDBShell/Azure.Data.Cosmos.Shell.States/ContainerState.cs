// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.States;

internal class ContainerState(string containerName, string databaseName, CosmosClient cosmosClient)
    : DatabaseState(databaseName, cosmosClient)
{
    public string ContainerName { get; init; } = containerName;

    public override Task<TR> AcceptAsync<TR, T>(IStateVisitor<TR, T> visitor, T data, CancellationToken token)
        where TR : class
    {
        return visitor.VisitContainerStateAsync(this, data, token);
    }
}
