// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

public class OutputRedirectionTests : IntegrationTestBase
{
    [Fact]
    public async Task Redirect_StdoutToFile_WritesOutput()
    {
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo \"hello\"");

        Assert.False(state.IsError);
        Assert.True(File.Exists(outputFile));
        var text = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("hello", text);
    }

    [Fact]
    public async Task Redirect_AppendToFile_AppendsOutput()
    {
        var outputFile = CaptureOutputFile();
        await RunScriptAsync("echo \"first\"");

        Shell.AppendOutRedirection = true;
        await RunScriptAsync("echo \"second\"");

        var text = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("first", text);
        Assert.Contains("second", text);
    }

    [Fact]
    public async Task Redirect_StderrToFile_WritesErrors()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"redir-err-{Guid.NewGuid():N}.txt");
        Shell.ErrOutRedirect = tempFile;

        try
        {
            var state = await Shell.ExecuteCommandAsync("nonexistent_cmd_for_error", CancellationToken.None);
            Assert.True(state.IsError);

            Assert.True(File.Exists(tempFile));
            var text = await File.ReadAllTextAsync(tempFile);
            Assert.NotEmpty(text);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
