// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;

/// <summary>
/// Unit tests for <see cref="ListCommand.BuildItemQueryText"/>. Pushes the
/// per-request limit down to the server with <c>SELECT TOP n</c> when no
/// client-side filter is in play, while preserving the unbounded query in the
/// cases where the shell still has to filter rows after the SDK returns them.
/// </summary>
public class ListCommandTests
{
    [Fact]
    public void BuildItemQueryText_NoLimit_NoFilter_UsesUnbounded()
    {
        Assert.Equal("SELECT * FROM c", ListCommand.BuildItemQueryText(null, null));
    }

    [Fact]
    public void BuildItemQueryText_WithLimit_NoFilter_UsesTop()
    {
        Assert.Equal("SELECT TOP 25 * FROM c", ListCommand.BuildItemQueryText(25, null));
    }

    [Fact]
    public void BuildItemQueryText_WithLimit_WildcardFilter_UsesTop()
    {
        // '*' is treated by ListCommand as "no filtering", so it is safe to
        // push the cap to the server.
        Assert.Equal("SELECT TOP 10 * FROM c", ListCommand.BuildItemQueryText(10, "*"));
    }

    [Fact]
    public void BuildItemQueryText_WithLimit_SubstringFilter_StaysUnbounded()
    {
        // A substring filter is applied in the shell against the partition or
        // custom key, so capping server-side rows would silently drop matching
        // items. Keep paging client-side.
        Assert.Equal("SELECT * FROM c", ListCommand.BuildItemQueryText(10, "active"));
    }

    [Fact]
    public void BuildItemQueryText_NoLimit_SubstringFilter_StaysUnbounded()
    {
        Assert.Equal("SELECT * FROM c", ListCommand.BuildItemQueryText(null, "active"));
    }

    [Fact]
    public void BuildItemQueryText_EmptyFilter_TreatedAsNoFilter()
    {
        Assert.Equal("SELECT TOP 5 * FROM c", ListCommand.BuildItemQueryText(5, string.Empty));
    }
}
