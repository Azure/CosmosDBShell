// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

public class DirTests : IntegrationTestBase
{
    private readonly string tempDir;

    public DirTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"dir-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "file1.txt"), "a");
        File.WriteAllText(Path.Combine(tempDir, "file2.csh"), "b");
        File.WriteAllText(Path.Combine(tempDir, "file3.txt"), "c");

        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "d");
    }

    [Fact]
    public async Task Dir_CurrentDirectory_ReturnsFiles()
    {
        var state = await RunScriptAsync($"dir -d \"{tempDir}\"");

        Assert.False(state.IsError);
        Assert.NotNull(state.Result);
        var json = (JsonElement)state.Result.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Json)!;
        var text = json.GetRawText();
        Assert.Contains("file1.txt", text);
        Assert.Contains("file2.csh", text);
    }

    [Fact]
    public async Task Dir_WithPattern_FiltersResults()
    {
        var state = await RunScriptAsync($"dir -d \"{tempDir}\" \"*.csh\"");

        Assert.False(state.IsError);
        Assert.NotNull(state.Result);
        var json = (JsonElement)state.Result.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Json)!;
        var text = json.GetRawText();
        Assert.Contains("file2.csh", text);
        Assert.DoesNotContain("file1.txt", text);
    }

    [Fact]
    public async Task Dir_Recursive_FindsNestedFiles()
    {
        var state = await RunScriptAsync($"dir -r -d \"{tempDir}\"");

        Assert.False(state.IsError);
        Assert.NotNull(state.Result);
        var json = (JsonElement)state.Result.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Json)!;
        var text = json.GetRawText();
        Assert.Contains("nested.txt", text);
    }

    [Fact]
    public async Task Dir_ListFormat_ReturnsFileMetadata()
    {
        var state = await RunScriptAsync($"dir -l -d \"{tempDir}\"");

        Assert.False(state.IsError);
        Assert.NotNull(state.Result);
        var json = (JsonElement)state.Result.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Json)!;
        var text = json.GetRawText();
        Assert.Contains("name", text);
    }

    public override void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }

        base.Dispose();
    }
}
