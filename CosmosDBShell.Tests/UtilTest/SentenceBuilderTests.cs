// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Util;

namespace CosmosShell.Tests.UtilTest;

public class SentenceBuilderTests
{
    [Fact]
    public void TestCommandDescriptions()
    {
        Assert.NotNull(LocalizableSentenceBuilder.ExecuteAndContinue);
        Assert.NotNull(LocalizableSentenceBuilder.ExecuteAndQuit);
        Assert.NotNull(LocalizableSentenceBuilder.ColorSystem);
        Assert.NotNull(LocalizableSentenceBuilder.ClearHistory);
        Assert.NotNull(LocalizableSentenceBuilder.ConnectionString);
        Assert.NotNull(LocalizableSentenceBuilder.Command);
    }
}
