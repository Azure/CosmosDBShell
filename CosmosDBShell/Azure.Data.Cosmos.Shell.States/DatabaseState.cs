// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.States;

using Azure.Data.Cosmos.Shell.Core;

internal class DatabaseState(string databaseName, CosmosClient cosmosClient, ArmCosmosContext? armContext = null) : ConnectedState(cosmosClient, armContext)
{
    public string DatabaseName { get; init; } = databaseName;

    public override Task<TR> AcceptAsync<TR, T>(IStateVisitor<TR, T> visitor, T data, CancellationToken token)
        where TR : class
    {
        return visitor.VisitDatabaseStateAsync(this, data, token);
    }
}
