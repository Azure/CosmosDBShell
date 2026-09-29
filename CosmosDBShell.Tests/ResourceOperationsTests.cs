// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.States;

public class ResourceOperationsTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, "/")]
    [InlineData("db", null, "/db")]
    [InlineData("db", "container", "/db/container")]
    public void GetCurrentLocation_ReturnsSharedLocationAsJson(string? database, string? container, string? expected)
    {
        var state = database is null
            ? (State)new DisconnectedState()
            : database.Length == 0
                ? new ConnectedState(null!)
                : container is null
                    ? new DatabaseState(database, null!)
                    : new ContainerState(container, database, null!);

        using var document = JsonDocument.Parse(ResourceOperations.GetCurrentLocation(state));
        var location = document.RootElement.GetProperty("currentLocation");
        Assert.Equal(expected, location.ValueKind == JsonValueKind.Null ? null : location.GetString());
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
