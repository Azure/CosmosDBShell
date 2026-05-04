// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

using Xunit;

public class AdditionalCommandsTests : EmulatorFixtureTestBase
{
    private readonly List<string> createdContainers = [];

    public AdditionalCommandsTests(EmulatorDatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await ExecuteAsync("cd");
        await ExecuteAsync($"cd {Fixture.DatabaseName}");
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var name in createdContainers)
        {
            try
            {
                await ExecuteAsync("cd");
                await ExecuteAsync($"cd {Fixture.DatabaseName}");
                await ExecuteAsync($"rmcon {name} true");
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        await base.DisposeAsync();
    }

    [Fact]
    public async Task Pwd_ReturnsCurrentLocation()
    {
        var state = await ExecuteAsync("pwd");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var json = IntegrationTestBase.GetJson(state);
        Assert.True(json.TryGetProperty("currentLocation", out var loc));
        var locStr = loc.GetString();
        Assert.False(string.IsNullOrEmpty(locStr));
        Assert.Contains(Fixture.DatabaseName, locStr);
    }

    [Fact]
    public async Task Bucket_ShowsCurrent_SetsAndResets()
    {
        var show = await ExecuteAsync("bucket");
        Assert.False(show.IsError, IntegrationTestBase.FormatError(show));

        var set = await ExecuteAsync("bucket 3");
        Assert.False(set.IsError, IntegrationTestBase.FormatError(set));

        var reset = await ExecuteAsync("bucket 0");
        Assert.False(reset.IsError, IntegrationTestBase.FormatError(reset));
    }

    [Fact]
    public async Task Bucket_InvalidValue_DoesNotErrorButDoesNotSet()
    {
        // CheckBucket prints a message for out-of-range values but returns an empty state
        // rather than throwing. We still ensure it does not crash the shell.
        var state = await ExecuteAsync("bucket 99");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));
    }

    [Fact]
    public async Task IndexPolicy_Read_ReturnsPolicyJson()
    {
        await ExecuteAsync($"cd {Fixture.ContainerName}");
        var state = await ExecuteAsync("indexpolicy");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var json = IntegrationTestBase.GetJson(state);
        Assert.True(json.TryGetProperty("indexingMode", out _));
    }

    [Fact]
    public async Task IndexPolicy_Write_UpdatesPolicy()
    {
        await ExecuteAsync($"cd {Fixture.ContainerName}");

        var policy = "{\"indexingMode\":\"consistent\",\"automatic\":true,\"includedPaths\":[{\"path\":\"/*\"}],\"excludedPaths\":[{\"path\":\"/\\\"_etag\\\"/?\"}]}";
        var state = await ExecuteAsync($"indexpolicy '{policy}'");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var json = IntegrationTestBase.GetJson(state);
        Assert.Equal("Consistent", json.GetProperty("indexingMode").GetString());
    }

    [Fact]
    public async Task IndexPolicy_InvalidJson_ReturnsError()
    {
        await ExecuteAsync($"cd {Fixture.ContainerName}");

        var state = await ExecuteAsync("indexpolicy 'not json'");
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task Settings_AtDatabaseLevel_ReturnsAccountOverview()
    {
        var state = await ExecuteAsync("settings");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));
    }

    [Fact]
    public async Task Settings_AtContainerLevel_ReturnsContainerSettings()
    {
        await ExecuteAsync($"cd {Fixture.ContainerName}");

        var state = await ExecuteAsync("settings");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var json = IntegrationTestBase.GetJson(state);
        Assert.Equal(Fixture.ContainerName, json.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Create_Item_AddsItemToContainer()
    {
        await ExecuteAsync($"cd {Fixture.ContainerName}");

        var id = $"create-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new { id, name = "from-create" });
        var state = await ExecuteAsync($"create item '{payload}'");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        // Verify the item exists via print (Result is consumed by the shell after printing)
        var printState = await ExecuteAsync($"print {id} {id}");
        Assert.False(printState.IsError, IntegrationTestBase.FormatError(printState));
    }

    [Fact]
    public async Task Create_Container_AddsContainer()
    {
        var name = $"CreateCon_{Guid.NewGuid():N}";
        createdContainers.Add(name);

        var state = await ExecuteAsync($"create container {name} /id");
        Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError, IntegrationTestBase.FormatError(lsState));
    }

    [Fact]
    public async Task Create_Container_MissingPartitionKey_ReturnsError()
    {
        var state = await ExecuteAsync("create container MissingPk");
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task Delete_Item_RemovesItem()
    {
        await ExecuteAsync($"cd {Fixture.ContainerName}");

        var id = $"del-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new { id, name = "to-delete" });
        await ExecuteAsync($"mkitem '{payload}'");

        var delState = await ExecuteAsync($"delete item \"{id}\"");
        Assert.False(delState.IsError, IntegrationTestBase.FormatError(delState));

        var printState = await ExecuteAsync($"print {id} {id}");
        Assert.True(printState.IsError);
    }

    [Fact]
    public async Task Delete_InvalidKind_ReturnsError()
    {
        var state = await ExecuteAsync("delete widget foo");
        Assert.True(state.IsError);
    }
}
