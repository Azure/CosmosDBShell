// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using CommandLine;
using Azure.Data.Cosmos.Shell.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    [Fact]
    public void TestLocalizableSentenceBuilder()
    {
        var builder = new LocalizableSentenceBuilder();
        Assert.NotNull(builder.RequiredWord());
        Assert.NotNull(builder.ErrorsHeadingText());
        Assert.NotNull(builder.UsageHeadingText());
        Assert.NotNull(builder.OptionGroupWord());
        Assert.NotNull(builder.HelpCommandText(true));
        Assert.NotNull(builder.HelpCommandText(false));
        Assert.NotNull(builder.VersionCommandText(true));
        Assert.NotNull(builder.VersionCommandText(false));
    }
}
