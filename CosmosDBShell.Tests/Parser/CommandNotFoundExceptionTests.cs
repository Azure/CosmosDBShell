// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using Azure.Data.Cosmos.Shell.Parser;

public class CommandNotFoundExceptionTests
{
    [Fact]
    public void Constructor_CarriesSourcePosition()
    {
        var ex = new CommandNotFoundException("AccountKey=abc", suggestion: null, start: 42, length: 14);

        Assert.Equal(42, ex.Start);
        Assert.Equal(14, ex.Length);
    }

    [Fact]
    public void Constructor_WithoutPosition_LeavesPositionNull()
    {
        var ex = new CommandNotFoundException("foo");

        Assert.Null(ex.Start);
        Assert.Null(ex.Length);
    }

    [Fact]
    public void Suggestion_ProducesDidYouMeanHint()
    {
        var ex = new CommandNotFoundException("conect", suggestion: "connect");

        Assert.NotNull(ex.Hint);
        Assert.Contains("connect", ex.Hint!, StringComparison.Ordinal);
    }

    [Fact]
    public void NoSuggestion_HasNoHint()
    {
        var ex = new CommandNotFoundException("foo_bar_baz", suggestion: null);

        Assert.Null(ex.Hint);
    }
}
