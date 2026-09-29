// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Runtime.CompilerServices;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;

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

    [Fact]
    public void State_NotifiesWhenClientChangesAtSameLocation()
    {
        using var shell = new ShellInterpreter();
        using var firstClient = CreateTestClient();
        using var secondClient = CreateTestClient();
        shell.State = new DatabaseState("db", firstClient);
        var changes = 0;
        shell.LocationChanged += () => changes++;

        shell.State = new DatabaseState("db", secondClient);
        Assert.Equal(1, changes);

        shell.State = new DisconnectedState();
    }

    [Fact]
    public void State_DoesNotNotifyWhenOnlyArmContextChanges()
    {
        using var shell = new ShellInterpreter();
        using var client = CreateTestClient();
        shell.State = new ConnectedState(client);
        var changes = 0;
        shell.LocationChanged += () => changes++;

        var armContext = (ArmCosmosContext)RuntimeHelpers.GetUninitializedObject(typeof(ArmCosmosContext));
        shell.State = new ConnectedState(client, armContext);
        Assert.Equal(0, changes);

        shell.State = new DisconnectedState();
    }

    private static CosmosClient CreateTestClient()
    {
        return new CosmosClient(
            "https://localhost:8081",
            Convert.ToBase64String(new byte[64]),
            new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
    }
}
