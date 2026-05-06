// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.States;

using Azure.Data.Cosmos.Shell.Core;

internal class ContainerState(string containerName, string databaseName, CosmosClient cosmosClient, ArmCosmosContext? armContext = null)
    : DatabaseState(databaseName, cosmosClient, armContext)
{
    public string ContainerName { get; init; } = containerName;

    public override Task<TR> AcceptAsync<TR, T>(IStateVisitor<TR, T> visitor, T data, CancellationToken token)
        where TR : class
    {
        return visitor.VisitContainerStateAsync(this, data, token);
    }
}
