// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;

public class ResourceOperationsTests
{
    [Theory]
    [InlineData(
        "connect \"AccountEndpoint=https://x.documents.azure.com:443/;AccountKey=abc123==;\"",
        "connect \"AccountEndpoint=https://x.documents.azure.com:443/;AccountKey=***;\"")]
    [InlineData(
        "connect AccountEndpoint=https://x;AccountKey=topsecret;DisableServerCertificateValidation=True",
        "connect AccountEndpoint=https://x;AccountKey=***;DisableServerCertificateValidation=True")]
    [InlineData(
        "ls",
        "ls")]
    [InlineData(
        "query \"SELECT * FROM c\"",
        "query \"SELECT * FROM c\"")]
    public void SanitizeHistoryEntry_RedactsAccountKey(string input, string expected)
    {
        Assert.Equal(expected, ResourceOperations.SanitizeHistoryEntry(input));
    }

    [Fact]
    public void GetCommandsCatalog_ReturnsArrayWithKnownCommand()
    {
        var json = ResourceOperations.GetCommandsCatalog();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToHashSet();

        Assert.Contains("query", names);
        Assert.DoesNotContain("rm", names);
        Assert.DoesNotContain("rmdb", names);
    }

    [Fact]
    public void GetConnection_ProducesParsableJson()
    {
        var json = ResourceOperations.GetConnection();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("connected", out _));
        Assert.True(doc.RootElement.TryGetProperty("currentLocation", out _));
    }

    [Fact]
    public void GetLocation_ProducesParsableJson()
    {
        var json = ResourceOperations.GetLocation();

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("location", out _));
    }

    [Fact]
    public void GetHistory_ProducesParsableJson()
    {
        var json = ResourceOperations.GetHistory();

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("count", out _));
        Assert.True(doc.RootElement.TryGetProperty("entries", out var entries));
        Assert.Equal(JsonValueKind.Array, entries.ValueKind);
    }
}
