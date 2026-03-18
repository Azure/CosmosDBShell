// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CosmosShell.Tests.UtilTest;

public class PatternMatcherTests
{
    [Fact]
    public void TestPattern()
    {
        var matcher = new PatternMatcher("a.txt");
        Assert.True(matcher.Match("a.txt"));
        Assert.False(matcher.Match("abcd.txt"));
        Assert.False(matcher.Match("1234.txt"));
    }

    [Fact]
    public void TestAnyChar()
    {
        var matcher = new PatternMatcher("???.txt");
        Assert.True(matcher.Match("abc.txt"));
        Assert.True(matcher.Match("123.txt"));
        Assert.False(matcher.Match("abcd.txt"));
        Assert.False(matcher.Match("1234.txt"));
    }

    [Fact]
    public void TestZeroOrMoreChar()
    {
        var matcher = new PatternMatcher("*");
        Assert.True(matcher.Match("abc.txt"));
        Assert.True(matcher.Match("abcd.txt"));
    }

    [Fact]
    public void TestExtension()
    {
        var matcher = new PatternMatcher("*.txt");
        Assert.True(matcher.Match("foo.txt"));
        Assert.False(matcher.Match("foo.txt.other"));
    }

    [Fact]
    public void TestStarDotStar()
    {
        var matcher = new PatternMatcher("*.*");
        Assert.True(matcher.Match("foo.txt"));
        Assert.True(matcher.Match("foo.txt.other"));
    }
}
