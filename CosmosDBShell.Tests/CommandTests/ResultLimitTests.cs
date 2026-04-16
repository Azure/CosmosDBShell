// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Util;

public class ResultLimitTests
{
    [Fact]
    public void ResolveMaxItemCount_UsesDefaultWhenNotSpecified()
    {
        Assert.Equal(ResultLimit.DefaultMaxItemCount, ResultLimit.ResolveMaxItemCount(null));
    }

    [Fact]
    public void ResolveMaxItemCount_UsesUnlimitedWhenDefaultIsNull()
    {
        Assert.Null(ResultLimit.ResolveMaxItemCount(null, defaultMaxItemCount: null));
    }

    [Fact]
    public void ResolveMaxItemCount_UsesExplicitPositiveValue()
    {
        Assert.Equal(25, ResultLimit.ResolveMaxItemCount(25));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ResolveMaxItemCount_UsesUnlimitedForZeroOrNegativeValues(int requestedMax)
    {
        Assert.Null(ResultLimit.ResolveMaxItemCount(requestedMax));
    }

    [Theory]
    [InlineData(99, 100, false)]
    [InlineData(100, 100, true)]
    [InlineData(101, 100, true)]
    public void IsLimitReached_ReturnsExpectedResult(int count, int limit, bool expected)
    {
        Assert.Equal(expected, ResultLimit.IsLimitReached(count, limit));
    }

    [Fact]
    public void IsLimitReached_ReturnsFalseWhenUnlimited()
    {
        Assert.False(ResultLimit.IsLimitReached(1000, null));
    }
}