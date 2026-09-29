// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;

public class ShellLocationChangedTests
{
    [Fact]
    public void State_NotifiesOnlyWhenLocationChanges()
    {
        using var shell = new ShellInterpreter();
        var changes = 0;
        shell.LocationChanged += () => changes++;

        shell.State = new DisconnectedState();
        Assert.Equal(0, changes);

        shell.State = new ConnectedState(null!);
        Assert.Equal(1, changes);

        shell.State = new DatabaseState("db", null!);
        Assert.Equal(2, changes);

        shell.State = new DatabaseState("db", null!);
        Assert.Equal(2, changes);

        shell.State = new ContainerState("container", "db", null!);
        Assert.Equal(3, changes);

        shell.State = new DisconnectedState();
        Assert.Equal(4, changes);
    }
}
