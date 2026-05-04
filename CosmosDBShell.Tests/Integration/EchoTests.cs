// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class EchoTests : IntegrationTestBase
{
    [Fact]
    public async Task Echo_SimpleMessage_PrintsText()
    {
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo \"hello world\"");

        Assert.False(state.IsError);
        var text = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
        Assert.Contains("hello world", text);
    }

    [Fact]
    public async Task Echo_MultipleArguments_JoinsWithSpaces()
    {
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo \"a\" \"b\" \"c\"");

        Assert.False(state.IsError);
        var text = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
        Assert.Contains("a b c", text);
    }

    [Fact]
    public async Task Echo_WithStringInterpolation()
    {
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("$x = \"World\"\necho $\"Hello $x\"");

        Assert.False(state.IsError);
        var text = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
        Assert.Contains("Hello World", text);
    }

    [Fact]
    public async Task Echo_PipedInput_PrintsPipedResult()
    {
        var outputFile = CaptureOutputFile();
        Shell.SetVariable("data", new ShellText("piped_value"));
        var state = await RunScriptAsync("echo $data | echo");

        Assert.False(state.IsError);
        var text = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
        Assert.Contains("piped_value", text);
    }
}
