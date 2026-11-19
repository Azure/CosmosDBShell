// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class VersionAndHelpTests : IntegrationTestBase
{
    [Theory]
    [InlineData("version", false)]
    [InlineData("help", false)]
    [InlineData("help echo", false)]
    [InlineData("help shortcuts", false)]
    [InlineData("help --plain shortcuts", false)]
    [InlineData("help nonexistent", true)]
    public async Task Commands_PreserveInternalResultTypes(string script, bool isError)
    {
        CaptureOutputFile();
        var state = await RunScriptAsync(script);

        Assert.Equal(isError, state.IsError);
        if (isError)
        {
            Assert.IsAssignableFrom<ErrorCommandState>(state);
        }
        else
        {
            Assert.IsType<ShellJson>(state.Result);
        }
    }
}
