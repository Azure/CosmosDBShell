// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Xunit.Sdk;

public class BasicCommandsTests : IntegrationTestBase
{
    [Fact]
    public async Task Cls_RunsWithoutError()
    {
        SkipIfNoConsole();
        var state = await RunScriptAsync("cls");

        Assert.False(state.IsError, FormatError(state));
    }

    [Fact]
    public async Task Clear_AliasOfCls_RunsWithoutError()
    {
        SkipIfNoConsole();
        var state = await RunScriptAsync("clear");

        Assert.False(state.IsError, FormatError(state));
    }

    private static void SkipIfNoConsole()
    {
        try
        {
            _ = Console.CursorLeft;
        }
        catch (IOException)
        {
            throw SkipException.ForSkip("Requires an interactive console");
        }
    }

    [Fact]
    public async Task Exit_StopsShell()
    {
        Assert.True(Shell.IsRunning);

        var state = await RunScriptAsync("exit");

        Assert.False(state.IsError, FormatError(state));
        Assert.False(Shell.IsRunning);
    }
}
