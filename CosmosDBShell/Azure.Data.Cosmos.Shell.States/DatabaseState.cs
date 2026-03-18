// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.States;

internal class DatabaseState(string databaseName, CosmosClient cosmosClient) : ConnectedState(cosmosClient)
{
    public string DatabaseName { get; init; } = databaseName;

    public override Task<TR> AcceptAsync<TR, T>(IStateVisitor<TR, T> visitor, T data, CancellationToken token)
        where TR : class
    {
        return visitor.VisitDatabaseStateAsync(this, data, token);
    }
}
