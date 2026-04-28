// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Util;

public class ErrorHandlingTests : IntegrationTestBase
{
    [Fact]
    public async Task UnknownCommand_ReturnsError()
    {
        var state = await RunScriptAsync("foo_bar_baz");

        Assert.True(state.IsError);
        Assert.Contains("foo_bar_baz", GetErrorMessage(state), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LsWithoutConnection_ReturnsError()
    {
        var state = await RunScriptAsync("ls");

        Assert.True(state.IsError);
        Assert.Equal(MessageService.GetString("error-not_connected_account"), GetErrorMessage(state));
    }

    [Fact]
    public async Task CdWithoutConnection_ReturnsError()
    {
        var state = await RunScriptAsync("cd mydb");

        Assert.True(state.IsError);
        Assert.Equal(MessageService.GetString("error-not_connected_account"), GetErrorMessage(state));
    }

    [Fact]
    public async Task DisconnectWithoutConnection_Succeeds()
    {
        var state = await RunScriptAsync("disconnect");

        Assert.False(state.IsError);
        var json = GetJson(state);
        Assert.True(json.TryGetProperty("disconnected", out var disconnected));
        Assert.False(disconnected.GetBoolean());
    }

    [Fact]
    public async Task QueryWithoutConnection_ReturnsError()
    {
        var state = await RunScriptAsync("query \"SELECT 1\"");

        Assert.True(state.IsError);
        Assert.Equal(MessageService.GetString("error-not_connected_account"), GetErrorMessage(state));
    }

    [Fact]
    public async Task SettingsWithoutConnection_ReturnsError()
    {
        var state = await RunScriptAsync("settings");

        Assert.True(state.IsError);
        Assert.Equal(MessageService.GetString("error-not_connected_account"), GetErrorMessage(state));
    }
}
