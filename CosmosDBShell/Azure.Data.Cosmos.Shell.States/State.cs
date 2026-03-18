// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.States;

internal abstract class State : IDisposable
{
    public abstract Task<TR> AcceptAsync<TR, T>(IStateVisitor<TR, T> visitor, T data, CancellationToken token)
        where TR : class;

    public virtual void Dispose()
    {
    }
}
