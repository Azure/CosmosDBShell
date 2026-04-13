// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Xunit;

[Trait("Category", "Emulator")]
[Collection("Emulator")]
public class ConnectionTests : EmulatorTestBase
{
    [Fact]
    public async Task Connect_ToEmulator_Succeeds()
    {
        await ConnectToEmulatorAsync();
        // If we get here without exception, the connection succeeded
    }

    [Fact]
    public async Task Connect_NoArgsWhenConnected_Behavior()
    {
        await ConnectToEmulatorAsync();
        var state = await RunScriptAsync("connect");

        // connect with no args when already connected shows connection info or errors —
        // either way it should not crash
        Assert.NotNull(state);
    }

    [Fact]
    public async Task Disconnect_AfterConnect_ReturnsToDisconnected()
    {
        await ConnectToEmulatorAsync();
        var state = await RunScriptAsync("disconnect");

        Assert.False(state.IsError);

        // After disconnect, ls should fail (not connected)
        var lsState = await RunScriptAsync("ls");
        Assert.True(lsState.IsError);
    }
}
