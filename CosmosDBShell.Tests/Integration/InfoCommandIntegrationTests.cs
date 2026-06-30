// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;
using System.Threading.Tasks;

using Xunit;

/// <summary>
/// Emulator integration tests for the <c>info</c> command. These verify that the
/// command runs without error against a real container, database, and account,
/// including the partition-distribution and detailed scan paths.
/// </summary>
public class InfoCommandIntegrationTests : ConnectedEmulatorTestBase
{
    [Fact]
    public async Task Info_Container_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd Items");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"a\\\" }\"");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"b\\\" }\"");

        var state = await ExecuteAsync("info");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Container_FormatJson_RedirectsJson()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd Items");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"a\\\" }\"");

        var output = await ExecuteWithOutputAsync("info --format json");
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal("Items", root.GetProperty("id").GetString());
        Assert.True(root.TryGetProperty("partitionKey", out var partitionKey));
        Assert.Equal(JsonValueKind.Array, partitionKey.ValueKind);
        Assert.True(root.TryGetProperty("documentCount", out _));
    }

    [Fact]
    public async Task Info_Container_RedirectWithoutFormat_DefaultsToJson()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd Items");

        var output = await ExecuteWithOutputAsync("info");
        using var document = JsonDocument.Parse(output);

        Assert.Equal("Items", document.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Info_Container_Partitions_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd Items");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"a\\\" }\"");

        var state = await ExecuteAsync("info --partitions");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Container_Detailed_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd Items");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"a\\\" }\"");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"b\\\" }\"");

        var state = await ExecuteAsync("info --detailed");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Database_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");

        var state = await ExecuteAsync($"info --database={dbName}");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Account_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");

        var state = await ExecuteAsync("info");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Account_Detailed_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd ..");

        var state = await ExecuteAsync("info --detailed");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    private async Task<string> ExecuteWithOutputAsync(string command)
    {
        var outputFile = Path.GetTempFileName();
        try
        {
            var state = await ExecuteAsync($"{command} > \"{ShellPath(outputFile)}\"");
            Assert.False(state.IsError, FormatError(state));

            Assert.True(File.Exists(outputFile), $"Expected output file at {outputFile}");
            return await ReadRedirectAsync(outputFile);
        }
        finally
        {
            try
            {
                File.Delete(outputFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup
            }
        }
    }
}
