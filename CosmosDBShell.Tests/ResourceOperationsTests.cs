// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using Azure.Data.Cosmos.Shell.Mcp;

public class ResourceOperationsTests
{
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
