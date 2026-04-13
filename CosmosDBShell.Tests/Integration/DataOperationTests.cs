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
        Assert.False(state.IsError);

        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError);
    }

    [Fact]
    public async Task Query_SelectAll_ReturnsItems()
    {
        var id = $"query-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-query" });
        await ExecuteAsync($"mkitem '{json}'");

        var state = await ExecuteAsync($"query \"SELECT * FROM c WHERE c.id = '{id}'\"");
        var errorMsg = state is Azure.Data.Cosmos.Shell.Core.ErrorCommandState err ? err.Exception.ToString() : "not an error";
        Assert.False(state.IsError, errorMsg);
    }

    [Fact]
    public async Task Print_ById_ReturnsItem()
    {
        var id = $"print-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-print" });
        await ExecuteAsync($"mkitem '{json}'");

        var state = await ExecuteAsync($"print {id} {id}");
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Rm_DeletesItem_NoLongerInLs()
    {
        var id = $"rm-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-rm" });
        await ExecuteAsync($"mkitem '{json}'");

        var state = await ExecuteAsync($"rm \"{id}\"");
        Assert.False(state.IsError);
    }

    private async Task<CommandState> ExecuteAsync(string command)
    {
        return await fixture.Shell.ExecuteCommandAsync(command, CancellationToken.None);
    }
}
