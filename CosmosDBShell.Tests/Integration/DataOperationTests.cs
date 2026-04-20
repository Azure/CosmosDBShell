// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;
using System.Threading;

using Azure.Data.Cosmos.Shell.Core;

using Xunit;
using Xunit.Sdk;

[Trait("Category", "Emulator")]
[Collection("Emulator")]
public class DataOperationTests : IClassFixture<EmulatorDatabaseFixture>, IAsyncLifetime
{
    private readonly EmulatorDatabaseFixture fixture;

    public DataOperationTests(EmulatorDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        if (!fixture.IsAvailable)
        {
            throw SkipException.ForSkip("Cosmos DB emulator not available");
        }

        // Navigate to the test container
        await ExecuteAsync("cd");
        await ExecuteAsync($"cd {fixture.DatabaseName}/{fixture.ContainerName}");
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task MkItem_CreatesItem_LsShowsIt()
    {
        var id = $"item-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "test-item" });
        var state = await ExecuteAsync($"mkitem '{json}'");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var mkItemJson = IntegrationTestBase.GetJson(state);
        Assert.Equal("success", mkItemJson.GetProperty("result").GetString());

        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError, IntegrationTestBase.FormatError(lsState));

        var lsJson = IntegrationTestBase.GetJson(lsState);
        var items = lsJson.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item =>
            item.GetProperty("id").GetString() == id &&
            item.GetProperty("name").GetString() == "test-item");
    }

    [Fact]
    public async Task Query_SelectAll_ReturnsItems()
    {
        var id = $"query-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-query" });
        await ExecuteAsync($"mkitem '{json}'");

        var state = await ExecuteAsync($"query \"SELECT * FROM c WHERE c.id = '{id}'\"");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var queryJson = IntegrationTestBase.GetJson(state);
        var items = queryJson.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(id, items[0].GetProperty("id").GetString());
        Assert.Equal("for-query", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Print_ById_ReturnsItem()
    {
        var id = $"print-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-print" });
        await ExecuteAsync($"mkitem '{json}'");

        var state = await ExecuteAsync($"print {id} {id}");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var printedItem = IntegrationTestBase.GetJson(state);
        Assert.Equal(id, printedItem.GetProperty("id").GetString());
        Assert.Equal("for-print", printedItem.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rm_DeletesItem_NoLongerInLs()
    {
        var id = $"rm-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-rm" });
        await ExecuteAsync($"mkitem '{json}'");

        var state = await ExecuteAsync($"rm \"{id}\"");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError, IntegrationTestBase.FormatError(lsState));

        var lsJson = IntegrationTestBase.GetJson(lsState);
        var items = lsJson.GetProperty("items");
        Assert.DoesNotContain(items.EnumerateArray(), item => item.GetProperty("id").GetString() == id);
    }

    private async Task<CommandState> ExecuteAsync(string command)
    {
        return await fixture.Shell.ExecuteCommandAsync(command, CancellationToken.None);
    }
}
