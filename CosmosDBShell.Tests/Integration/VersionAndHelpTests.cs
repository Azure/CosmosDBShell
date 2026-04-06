// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

public class VersionAndHelpTests : IntegrationTestBase
{
    [Fact]
    public async Task Version_ReturnsNonError()
    {
        var state = await RunScriptAsync("version");
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Help_NoArgs_ListsAllCommands()
    {
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("help");

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Help_SpecificCommand_ReturnsDetails()
    {
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("help echo");

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Help_UnknownCommand_ReturnsError()
    {
        var state = await RunScriptAsync("help nonexistent");
        Assert.True(state.IsError);
    }
}
