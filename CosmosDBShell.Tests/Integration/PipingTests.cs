// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class PipingTests : IntegrationTestBase
{
    [Fact]
    public async Task Pipe_EchoToEcho_PassesResult()
    {
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo \"hello\" | echo");

        Assert.False(state.IsError);
        var text = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("hello", text);
    }

    [Fact]
    public async Task Pipe_JsonThroughJq_TransformsOutput()
    {
        ExternalToolCheck.SkipIfMissing("jq");
        Shell.SetVariable("data", new ShellJson(JsonSerializer.SerializeToElement(new { name = "test", value = 42 })));
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo $data | jq .name");

        Assert.False(state.IsError);
        var text = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("test", text);
    }

    [Fact]
    public async Task Pipe_DirToFtab_FormatsAsTable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pipe-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "a.txt"), "content");
        File.WriteAllText(Path.Combine(tempDir, "b.txt"), "content");

        try
        {
            var outputFile = CaptureOutputFile();
            var state = await RunScriptAsync($"dir -l -d \"{ShellPath(tempDir)}\" | ftab -f name");

            Assert.False(state.IsError);
            var text = await File.ReadAllTextAsync(outputFile);
            Assert.Contains("name", text);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Pipe_ChainMultipleCommands()
    {
        ExternalToolCheck.SkipIfMissing("jq");
        Shell.SetVariable("data", new ShellJson(JsonSerializer.SerializeToElement(new { msg = "chain" })));
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo $data | jq .msg");

        Assert.False(state.IsError);
        var text = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("chain", text);
    }

    [Fact]
    public async Task Pipe_FailureInMiddle_PropagatesError()
    {
        var state = await RunScriptAsync("echo \"test\" | nonexistent_command | echo");
        Assert.True(state.IsError);
    }
}
