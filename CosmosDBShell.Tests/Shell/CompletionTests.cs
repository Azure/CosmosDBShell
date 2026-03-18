// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;
using RadLine;

public class CompletionTests
{
    [Fact]
    public void TestCompleteKnown()
    {
        var completion = CosmosCompleteCommand.GetCompletion(ShellInterpreter.Instance, "he", AutoComplete.Next);
        Assert.Equal("help", completion);
    }

    [Fact]
    public void TestUnknownCommand()
    {
        var completion = CosmosCompleteCommand.GetCompletion(ShellInterpreter.Instance, "evlevlevlevlelv", AutoComplete.Next);
        Assert.Null(completion);
    }
}
