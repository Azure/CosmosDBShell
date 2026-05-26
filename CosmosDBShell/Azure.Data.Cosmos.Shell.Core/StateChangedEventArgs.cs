// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.States;

/// <summary>
/// Event data describing a transition of <see cref="ShellInterpreter.State"/>.
/// </summary>
internal sealed class StateChangedEventArgs : EventArgs
{
    public StateChangedEventArgs(State? previousState, State newState)
    {
        this.PreviousState = previousState;
        this.NewState = newState;
    }

    /// <summary>
    /// Gets the previous state. May be <c>null</c> during the first transition
    /// in the constructor.
    /// </summary>
    public State? PreviousState { get; }

    /// <summary>
    /// Gets the new state.
    /// </summary>
    public State NewState { get; }
}
