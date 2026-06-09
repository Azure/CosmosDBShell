// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using Azure.Data.Cosmos.Shell.Mcp;

public class EmbeddedResourceLoaderTests
{
    [Fact]
    public void Load_ReturnsContent_ForExistingResource()
    {
        var content = EmbeddedResourceLoader.Load(typeof(ToolOperations).Assembly, "ServerInstructions.md");

        Assert.False(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public void Load_MatchesSuffixCaseInsensitively()
    {
        var content = EmbeddedResourceLoader.Load(typeof(ToolOperations).Assembly, "serverinstructions.md");

        Assert.False(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public void Load_Throws_ForMissingResource()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => EmbeddedResourceLoader.Load(typeof(ToolOperations).Assembly, "does-not-exist.md"));

        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
