// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;

public class DataPlaneCosmosResourceOperationsTests
{
    [Fact]
    public void IndexingPolicy_RoundTrip_PreservesSdkCollections()
    {
        const string json =
            "{\"indexingMode\":\"consistent\",\"automatic\":true," +
            "\"includedPaths\":[{\"path\":\"/*\"}]," +
            "\"excludedPaths\":[{\"path\":\"/\\\"_etag\\\"/?\"}]," +
            "\"spatialIndexes\":[{\"path\":\"/location/*\",\"types\":[\"Point\"]}]}";

        var policy = DataPlaneCosmosResourceOperations.ParseIndexingPolicy(json);
        var roundTrip = DataPlaneCosmosResourceOperations.SerializeIndexingPolicy(policy);

        using var document = JsonDocument.Parse(roundTrip);
        var root = document.RootElement;
        Assert.Equal("/*", root.GetProperty("includedPaths")[0].GetProperty("path").GetString());
        Assert.Equal("/\"_etag\"/?", root.GetProperty("excludedPaths")[0].GetProperty("path").GetString());
        var spatial = root.GetProperty("spatialIndexes")[0];
        Assert.Equal("/location/*", spatial.GetProperty("path").GetString());
        Assert.Equal("Point", spatial.GetProperty("types")[0].GetString());
        Assert.False(spatial.TryGetProperty("spatialTypes", out _));
    }
}