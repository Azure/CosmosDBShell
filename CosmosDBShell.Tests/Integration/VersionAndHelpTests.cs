// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

public class VersionAndHelpTests : IntegrationTestBase
{
    [Fact]
    public async Task Version_ReturnsStructuredJson()
    {
        var state = await RunScriptAsync("version");

        Assert.False(state.IsError);

        var json = GetJson(state);
        Assert.True(json.TryGetProperty("version", out var version));
        Assert.False(string.IsNullOrWhiteSpace(version.GetString()));
        Assert.True(json.TryGetProperty("mcpEnabled", out var mcpEnabled));
        Assert.False(mcpEnabled.GetBoolean());
        Assert.Equal("off", json.GetProperty("mcpStatus").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("mcpPort").ValueKind);
    }

    [Fact]
    public async Task Help_NoArgs_ReturnsCommandAndStatementMetadata()
    {
        var state = await RunScriptAsync("help");

        Assert.False(state.IsError);

        var json = GetJson(state);
        Assert.Contains("available", json.GetProperty("help").GetString(), StringComparison.OrdinalIgnoreCase);

        var commands = json.GetProperty("commands");
        Assert.True(commands.GetArrayLength() > 0);
        Assert.Contains(commands.EnumerateArray(), command => command.GetProperty("command").GetString() == "help");
        Assert.Contains(commands.EnumerateArray(), command => command.GetProperty("command").GetString() == "version");

        var statements = json.GetProperty("statements");
        Assert.True(statements.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Help_SpecificCommand_ReturnsDetailedCommandMetadata()
    {
        var state = await RunScriptAsync("help echo");

        Assert.False(state.IsError);

        var json = GetJson(state);
        Assert.Equal("echo", json.GetProperty("command").GetString());
        Assert.Contains("message", json.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);

        var parameters = json.GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "messages");
    }

    [Fact]
    public async Task Help_UnknownCommand_ReturnsError()
    {
        var state = await RunScriptAsync("help nonexistent");

        Assert.True(state.IsError);
        Assert.Contains("nonexistent", GetErrorMessage(state), StringComparison.OrdinalIgnoreCase);
    }
}
