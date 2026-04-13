// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Net.Http;
using System.Threading;

using Azure.Data.Cosmos.Shell.Core;

using Xunit;
using Xunit.Sdk;

[Trait("Category", "Emulator")]
[Collection("Emulator")]
public class NavigationTests : IClassFixture<EmulatorDatabaseFixture>, IAsyncLifetime
{
    private readonly EmulatorDatabaseFixture fixture;

    public NavigationTests(EmulatorDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        if (!fixture.IsAvailable)
        {
            throw SkipException.ForSkip("Cosmos DB emulator not available");
        }

        // Navigate back to the connected root before each test
        await NavigateToRootAsync();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Ls_AtRoot_ListsDatabases()
    {
        await NavigateToRootAsync();
        var state = await ExecuteAsync("ls");

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Cd_ToDatabase_NavigatesToDatabase()
    {
        await NavigateToRootAsync();
        var state = await ExecuteAsync($"cd {fixture.DatabaseName}");

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Cd_ToContainer_NavigatesToContainer()
    {
        await NavigateToRootAsync();
        var state = await ExecuteAsync($"cd {fixture.DatabaseName}/{fixture.ContainerName}");

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Cd_DotDot_GoesUp()
    {
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {fixture.DatabaseName}/{fixture.ContainerName}");

        var state = await ExecuteAsync("cd ..");
        Assert.False(state.IsError);

        // Should be at database level now, ls should show containers
        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError);
    }

    [Fact]
    public async Task Cd_NoArgs_ReturnsToRoot()
    {
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {fixture.DatabaseName}/{fixture.ContainerName}");

        var state = await ExecuteAsync("cd");
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Ls_InDatabase_ListsContainers()
    {
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {fixture.DatabaseName}");

        var state = await ExecuteAsync("ls");
        Assert.False(state.IsError);
    }

    private async Task<CommandState> ExecuteAsync(string command)
    {
        return await fixture.Shell.ExecuteCommandAsync(command, CancellationToken.None);
    }

    private async Task NavigateToRootAsync()
    {
        try
        {
            await ExecuteAsync("cd");
        }
        catch
        {
            // Best effort
        }
    }
}
