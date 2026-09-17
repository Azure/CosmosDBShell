// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

public class DataPlaneCosmosResourceOperationsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("none", null)]
    [InlineData(null, false)]
    public void IndexingPolicy_RoundTrip_PreservesSdkCollections(string? mode, bool? automatic)
    {
        const string json =
            "{\"indexingMode\":\"consistent\",\"automatic\":true," +
            "\"includedPaths\":[{\"path\":\"/*\"}]," +
            "\"excludedPaths\":[{\"path\":\"/\\\"_etag\\\"/?\"}]," +
            "\"compositeIndexes\":[[{\"path\":\"/name\",\"order\":\"ascending\"},{\"path\":\"/age\",\"order\":\"descending\"}]]," +
            "\"spatialIndexes\":[{\"path\":\"/location/*\",\"types\":[\"Point\"]}]," +
            "\"vectorIndexes\":[{\"path\":\"/embedding\",\"type\":\"quantizedFlat\"}]," +
            "\"fullTextIndexes\":[{\"path\":\"/text\"}]}";

        var policy = DataPlaneCosmosResourceOperations.ParseIndexingPolicy(json);
        var roundTrip = DataPlaneCosmosResourceOperations.SerializeIndexingPolicy(policy);
        if (mode is not null || automatic.HasValue)
        {
            var updated = IndexCommand.ApplySettings(roundTrip, mode, automatic);
            roundTrip = DataPlaneCosmosResourceOperations.SerializeIndexingPolicy(
                DataPlaneCosmosResourceOperations.ParseIndexingPolicy(updated));
        }

        using var document = JsonDocument.Parse(roundTrip);
        var root = document.RootElement;
        Assert.Equal(mode ?? "consistent", root.GetProperty("indexingMode").GetString(), ignoreCase: true);
        Assert.Equal(automatic ?? true, root.GetProperty("automatic").GetBoolean());
        var included = Assert.Single(root.GetProperty("includedPaths").EnumerateArray());
        Assert.Equal("/*", included.GetProperty("path").GetString());
        var excluded = Assert.Single(root.GetProperty("excludedPaths").EnumerateArray());
        Assert.Equal("/\"_etag\"/?", excluded.GetProperty("path").GetString());
        var composite = Assert.Single(root.GetProperty("compositeIndexes").EnumerateArray());
        Assert.Equal(2, composite.GetArrayLength());
        Assert.Equal("/name", composite[0].GetProperty("path").GetString());
        Assert.Equal("ascending", composite[0].GetProperty("order").GetString(), ignoreCase: true);
        Assert.Equal("/age", composite[1].GetProperty("path").GetString());
        Assert.Equal("descending", composite[1].GetProperty("order").GetString(), ignoreCase: true);
        var spatial = Assert.Single(root.GetProperty("spatialIndexes").EnumerateArray());
        Assert.Equal("/location/*", spatial.GetProperty("path").GetString());
        Assert.Equal("Point", Assert.Single(spatial.GetProperty("types").EnumerateArray()).GetString());
        Assert.False(spatial.TryGetProperty("spatialTypes", out _));
        var vector = Assert.Single(root.GetProperty("vectorIndexes").EnumerateArray());
        Assert.Equal("/embedding", vector.GetProperty("path").GetString());
        Assert.Equal("quantizedFlat", vector.GetProperty("type").GetString(), ignoreCase: true);
        var fullText = Assert.Single(root.GetProperty("fullTextIndexes").EnumerateArray());
        Assert.Equal("/text", fullText.GetProperty("path").GetString());
    }
}