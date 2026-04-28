// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

using Xunit;

public class DataOperationTests : EmulatorFixtureTestBase
{
    public DataOperationTests(EmulatorDatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await ExecuteAsync("cd");
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");
    }

    [Fact]
    public async Task MkItem_CreatesItem_LsShowsIt()
    {
        var id = $"item-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "test-item" });
        var mkOutput = await ExecuteWithOutputAsync($"mkitem '{json}'");
        var mkJson = JsonDocument.Parse(mkOutput).RootElement;
        Assert.Equal("success", mkJson.GetProperty("result").GetString());

        var lsOutput = await ExecuteWithOutputAsync("ls");
        var lsJson = JsonDocument.Parse(lsOutput).RootElement;
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

        var output = await ExecuteWithOutputAsync($"query \"SELECT * FROM c WHERE c.id = '{id}'\"");
        var queryJson = JsonDocument.Parse(output).RootElement;
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

        var output = await ExecuteWithOutputAsync($"print {id} {id}");
        var printedItem = JsonDocument.Parse(output).RootElement;
        Assert.Equal(id, printedItem.GetProperty("id").GetString());
        Assert.Equal("for-print", printedItem.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rm_DeletesItem_NoLongerInLs()
    {
        var id = $"rm-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-rm" });
        await ExecuteAsync($"mkitem '{json}'");

        var rmState = await ExecuteAsync($"rm \"{id}\"");
        Assert.False(rmState.IsError, IntegrationTestBase.FormatError(rmState));

        var lsOutput = await ExecuteWithOutputAsync("ls");
        var lsJson = JsonDocument.Parse(lsOutput).RootElement;
        var items = lsJson.GetProperty("items");
        Assert.DoesNotContain(items.EnumerateArray(), item => item.GetProperty("id").GetString() == id);
    }
}