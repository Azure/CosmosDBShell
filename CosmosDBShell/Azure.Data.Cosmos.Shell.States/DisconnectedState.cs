// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.States;

internal class DisconnectedState : State
{
    public override Task<TR> AcceptAsync<TR, T>(IStateVisitor<TR, T> visitor, T data, CancellationToken token)
        where TR : class
    {
        return visitor.VisitDisconnectedStateAsync(this, data, token);
    }
}
