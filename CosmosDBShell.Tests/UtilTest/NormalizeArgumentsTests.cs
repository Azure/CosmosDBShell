// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

public class NormalizeArgumentsTests
{
    [Fact]
    public void EmptyArgs_ReturnsEmpty()
    {
        Assert.Empty(Program.NormalizeArguments([]));
    }

    [Fact]
    public void NonCommandArgs_PassThroughUnchanged()
    {
        var input = new[] { "--connect", "endpoint", "--verbose" };
        Assert.Equal(input, Program.NormalizeArguments(input));
    }

    [Fact]
    public void DashC_ConsumesRemainingTokensAsSingleString()
    {
        var result = Program.NormalizeArguments(["-c", "help", "mkitem"]);
        Assert.Equal(["-c", "help mkitem"], result);
    }

    [Fact]
    public void DashK_ConsumesRemainingTokensAsSingleString()
    {
        var result = Program.NormalizeArguments(["-k", "help", "mkitem"]);
        Assert.Equal(["-k", "help mkitem"], result);
    }

    [Theory]
    [InlineData("/c")]
    [InlineData("/C")]
    public void SlashC_IsTranslatedToDashC(string token)
    {
        var result = Program.NormalizeArguments([token, "help", "mkitem"]);
        Assert.Equal(["-c", "help mkitem"], result);
    }

    [Theory]
    [InlineData("/k")]
    [InlineData("/K")]
    public void SlashK_IsTranslatedToDashK(string token)
    {
        var result = Program.NormalizeArguments([token, "help", "mkitem"]);
        Assert.Equal(["-k", "help mkitem"], result);
    }

    [Fact]
    public void AppOptionsBeforeDashC_ArePreserved()
    {
        var result = Program.NormalizeArguments(
            ["--connect", "endpoint", "-c", "help", "mkitem"]);
        Assert.Equal(["--connect", "endpoint", "-c", "help mkitem"], result);
    }

    [Fact]
    public void DashCWithoutTail_LeavesDashCAlone()
    {
        var result = Program.NormalizeArguments(["-c"]);
        Assert.Equal(["-c"], result);
    }

    [Fact]
    public void DashCWithQuotedSingleToken_StaysSingleToken()
    {
        var result = Program.NormalizeArguments(["-c", "help mkitem"]);
        Assert.Equal(["-c", "help mkitem"], result);
    }

    [Fact]
    public void TokensThatLookLikeOptions_AfterDashC_AreAbsorbed()
    {
        var result = Program.NormalizeArguments(
            ["-c", "seed.csh", "--connect", "xyz"]);
        Assert.Equal(["-c", "seed.csh --connect xyz"], result);
    }

    [Fact]
    public void TakePreCommandArgs_ReturnsEverythingBeforeDashC()
    {
        var result = Program.TakePreCommandArgs(
            ["--verbose", "-c", "help"]);
        Assert.Equal(["--verbose"], result);
    }

    [Fact]
    public void TakePreCommandArgs_ReturnsEverythingBeforeDashK()
    {
        var result = Program.TakePreCommandArgs(
            ["--connect", "ep", "-k", "help"]);
        Assert.Equal(["--connect", "ep"], result);
    }

    [Fact]
    public void TakePreCommandArgs_NoCommandMarker_ReturnsAll()
    {
        var input = new[] { "--connect", "endpoint", "--verbose" };
        Assert.Equal(input, Program.TakePreCommandArgs(input));
    }

    [Fact]
    public void TakePreCommandArgs_DashCFirst_ReturnsEmpty()
    {
        var result = Program.TakePreCommandArgs(["-c", "--help"]);
        Assert.Empty(result);
    }
}
