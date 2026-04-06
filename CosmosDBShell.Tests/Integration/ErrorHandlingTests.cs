// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

public class ErrorHandlingTests : IntegrationTestBase
{
    [Fact]
    public async Task UnknownCommand_ReturnsError()
    {
        var state = await RunScriptAsync("foo_bar_baz");
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task LsWithoutConnection_ReturnsError()
    {
        var state = await RunScriptAsync("ls");
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task CdWithoutConnection_ReturnsError()
    {
        var state = await RunScriptAsync("cd mydb");
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task DisconnectWithoutConnection_Succeeds()
    {
        var state = await RunScriptAsync("disconnect");
        // disconnect when not connected is not an error — it returns a result indicating no active connection
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task QueryWithoutConnection_ReturnsError()
    {
        var state = await RunScriptAsync("query \"SELECT 1\"");
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task SettingsWithoutConnection_ReturnsError()
    {
        var state = await RunScriptAsync("settings");
        Assert.True(state.IsError);
    }
}
