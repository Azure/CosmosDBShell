// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Contract;

using System.Text.Json;

using CosmosShell.Tests.Integration;

/// <summary>
/// Pins the machine-readable JSON contract that CI pipelines, scripts, and MCP clients
/// consume. A failure here means the output contract changed: update the expectation and
/// record the change under "Breaking changes" in CHANGELOG.md.
/// </summary>
public class JsonResultContractTests : IntegrationTestBase
{
    /// <summary>
    /// Commands whose result can be produced without a Cosmos DB connection, so the shared
    /// conventions stay enforced in the offline CI job.
    /// </summary>
    public static TheoryData<string> OfflineCommands =>
    [
        "version",
        "pwd",
        "help",
        "help query",
        "theme list",
        "theme current",
        "theme show",
        "dir",
        "connect",
        "disconnect",
    ];

    [Theory]
    [MemberData(nameof(OfflineCommands))]
    public async Task OfflineCommand_FollowsResultConventions(string script)
    {
        await RunAndGetJsonAsync(script);
    }

    [Fact]
    public async Task Version_Shape()
    {
        var json = await RunAndGetJsonAsync("version");

        AssertShape(json, "version", "mcpEnabled", "mcpPort", "mcpStatus", "repository");
    }

    [Fact]
    public async Task Pwd_Shape()
    {
        var json = await RunAndGetJsonAsync("pwd");

        AssertShape(json, "type", "database", "container", "currentLocation");
        Assert.Equal("location", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Help_Shape()
    {
        var json = await RunAndGetJsonAsync("help");

        AssertShape(json, "help", "commands", "statements");
    }

    [Fact]
    public async Task HelpForCommand_Shape()
    {
        var json = await RunAndGetJsonAsync("help query");

        AssertShape(
            json,
            "command",
            "description",
            "aliases",
            "additionalDescriptionForMcp",
            "parameters",
            "options",
            "statements");
    }

    [Fact]
    public async Task ThemeList_UsesListEnvelope()
    {
        var json = await RunAndGetJsonAsync("theme list");

        AssertShape(json, "type", "values");
        Assert.Equal("theme", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ThemeCurrent_Shape()
    {
        var json = await RunAndGetJsonAsync("theme current");

        AssertShape(json, "type", "id", "active");
        Assert.True(json.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Dir_UsesListEnvelope()
    {
        var json = await RunAndGetJsonAsync("dir");

        AssertShape(json, "type", "values");
        Assert.Equal("file", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Connect_WhenDisconnected_Shape()
    {
        var json = await RunAndGetJsonAsync("connect");

        AssertShape(json, "type", "connected");
        Assert.False(json.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Disconnect_WhenDisconnected_Shape()
    {
        var json = await RunAndGetJsonAsync("disconnect");

        AssertShape(json, "type", "disconnected");
        Assert.False(json.GetProperty("disconnected").GetBoolean());
    }

    private static void AssertShape(JsonElement element, params string[] expectedNames)
    {
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expected = expectedNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static void AssertConventions(JsonElement element)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);

        foreach (var property in element.EnumerateObject())
        {
            // A key that encodes its own value (for example "connected state": "<endpoint>")
            // cannot be addressed reliably by a client; keys must be stable identifiers.
            Assert.DoesNotContain(" ", property.Name);
        }

        // Listings share one envelope so table/CSV rendering and clients locate rows the same way.
        if (element.TryGetProperty("values", out var values))
        {
            Assert.Equal(JsonValueKind.Array, values.ValueKind);
            Assert.True(
                element.TryGetProperty("type", out _),
                "A payload carrying 'values' must also carry a 'type' discriminator.");
        }

        // A content-free acknowledgement tells automation nothing about what happened.
        if (element.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
        {
            Assert.NotEqual("success", result.GetString());
        }
    }

    private async Task<JsonElement> RunAndGetJsonAsync(string script)
    {
        var state = await RunScriptAsync(script);

        Assert.False(state.IsError, FormatError(state));

        var json = GetJson(state);
        AssertConventions(json);
        return json;
    }
}
