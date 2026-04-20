// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;

using Xunit;

[Trait("Category", "Emulator")]
[Collection("Emulator")]
public class ConnectionTests : EmulatorTestBase
{
    [Fact]
    public async Task Connect_ToEmulator_Succeeds()
    {
        await ConnectToEmulatorAsync();

        Assert.IsType<ConnectedState>(Shell.State);
    }

    [Fact]
    public async Task Connect_NoArgsWhenConnected_ReturnsConnectionInfoJson()
    {
        await ConnectToEmulatorAsync();
        var state = await RunScriptAsync("connect");

        Assert.False(state.IsError);

        var json = GetJson(state);
        Assert.True(json.GetProperty("connected").GetBoolean());
        Assert.Equal("/", json.GetProperty("currentLocation").GetString());
        Assert.Contains("localhost", json.GetProperty("endpoint").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("accountId").GetString()));
    }

    [Fact]
    public async Task Disconnect_AfterConnect_ReturnsToDisconnected()
    {
        await ConnectToEmulatorAsync();
        var state = await RunScriptAsync("disconnect");

        Assert.False(state.IsError);

        var json = GetJson(state);
        Assert.True(json.GetProperty("disconnected").GetBoolean());
        Assert.Contains("localhost", json.GetProperty("endpoint").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.IsType<DisconnectedState>(Shell.State);

        // After disconnect, ls should fail (not connected)
        var lsState = await RunScriptAsync("ls");
        Assert.True(lsState.IsError);
        Assert.Contains("not connected", GetErrorMessage(lsState), StringComparison.OrdinalIgnoreCase);
    }
}
