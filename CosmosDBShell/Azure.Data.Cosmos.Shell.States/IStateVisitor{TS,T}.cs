// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.States;

internal interface IStateVisitor<TS, T>
{
    Task<TS> VisitConnectedStateAsync(ConnectedState state, T data, CancellationToken token);

    Task<TS> VisitContainerStateAsync(ContainerState state, T data, CancellationToken token);

    Task<TS> VisitDatabaseStateAsync(DatabaseState state, T data, CancellationToken token);

    Task<TS> VisitDisconnectedStateAsync(DisconnectedState state, T data, CancellationToken token);
}
