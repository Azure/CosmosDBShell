// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.States;

using Azure.Data.Cosmos.Shell.Core;

internal class ConnectedState(CosmosClient cosmosClient, ArmCosmosContext? armContext = null) : State
{
    public CosmosClient Client { get; init; } = cosmosClient;

    public ArmCosmosContext? ArmContext { get; init; } = armContext;

    public override Task<TR> AcceptAsync<TR, T>(IStateVisitor<TR, T> visitor, T data, CancellationToken token)
        where TR : class
    {
        return visitor.VisitConnectedStateAsync(this, data, token);
    }

    public override void Dispose()
    {
        this.Client.Dispose();
    }
}
