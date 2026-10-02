// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;

public class ResourceOperationsTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, "/")]
    [InlineData("db", null, "/db")]
    [InlineData("db", "container", "/db/container")]
    public void GetCurrentLocation_ReturnsSharedLocationAsJson(string? database, string? container, string? expected)
    {
        using var client = database is null
            ? null
            : new CosmosClient(
                "https://localhost:8081",
                Convert.ToBase64String(new byte[64]),
                new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        var state = database is null
            ? (State)new DisconnectedState()
            : database.Length == 0
                ? new ConnectedState(client!)
                : container is null
                    ? new DatabaseState(database, client!)
                    : new ContainerState(container, database, client!);

        using var document = JsonDocument.Parse(ResourceOperations.GetCurrentLocation(state));
        var location = document.RootElement.GetProperty("currentLocation");
        Assert.Equal(expected, location.ValueKind == JsonValueKind.Null ? null : location.GetString());
        var endpoint = document.RootElement.GetProperty("currentAccountEndpoint");
        Assert.Equal(client?.Endpoint.ToString(), endpoint.ValueKind == JsonValueKind.Null ? null : endpoint.GetString());
    }

    [Fact]
    public void GetCurrentLocation_ReflectsAccountChangeWithoutNavigationChange()
    {
        var key = Convert.ToBase64String(new byte[64]);
        using var first = new CosmosClient(
            "https://first.documents.azure.com:443",
            key,
            new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        using var second = new CosmosClient(
            "https://second.documents.azure.com:443",
            key,
            new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });

        using var before = JsonDocument.Parse(ResourceOperations.GetCurrentLocation(new DatabaseState("db", first)));
        using var after = JsonDocument.Parse(ResourceOperations.GetCurrentLocation(new DatabaseState("db", second)));

        Assert.Equal("/db", before.RootElement.GetProperty("currentLocation").GetString());
        Assert.Equal("/db", after.RootElement.GetProperty("currentLocation").GetString());
        Assert.Equal(first.Endpoint.ToString(), before.RootElement.GetProperty("currentAccountEndpoint").GetString());
        Assert.Equal(second.Endpoint.ToString(), after.RootElement.GetProperty("currentAccountEndpoint").GetString());
    }

    [Fact]
    public void GetScriptingGuide_ReturnsEmbeddedProgrammingMarkdown()
    {
        var guide = ResourceOperations.GetScriptingGuide();

        Assert.False(string.IsNullOrWhiteSpace(guide));
        Assert.Contains("#", guide);
    }

    [Fact]
    public void GetQueryLanguageReference_ReturnsEmbeddedQueryLanguageMarkdown()
    {
        var reference = ResourceOperations.GetQueryLanguageReference();

        Assert.False(string.IsNullOrWhiteSpace(reference));
        Assert.Contains("SELECT", reference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetScriptingGuide_IsStableAcrossCalls()
    {
        Assert.Equal(ResourceOperations.GetScriptingGuide(), ResourceOperations.GetScriptingGuide());
    }
}
