// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

public class CatTests : IntegrationTestBase
{
    [Fact]
    public async Task Cat_ValidFile_ReturnsContents()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"cat-test-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "file content here", TestContext.Current.CancellationToken);

        try
        {
            var outputFile = CaptureOutputFile();
            var state = await RunScriptAsync($"cat \"{ShellPath(tempFile)}\"");

            Assert.False(state.IsError);
            var text = await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
            Assert.Contains("file content here", text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Cat_MissingFile_ReturnsError()
    {
        var state = await RunScriptAsync("cat \"/nonexistent/path/file.txt\"");
        Assert.True(state.IsError);
    }
}
