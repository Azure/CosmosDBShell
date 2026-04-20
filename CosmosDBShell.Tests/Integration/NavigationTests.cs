// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Xunit;

public class NavigationTests : EmulatorFixtureTestBase
{
    public NavigationTests(EmulatorDatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        // Navigate back to the connected root before each test
        await NavigateToRootAsync();
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
        var state = await ExecuteAsync($"cd {Fixture.DatabaseName}");

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Cd_ToContainer_NavigatesToContainer()
    {
        await NavigateToRootAsync();
        var state = await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Cd_DotDot_GoesUp()
    {
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");

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
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");

        var state = await ExecuteAsync("cd");
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Ls_InDatabase_ListsContainers()
    {
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {Fixture.DatabaseName}");

        var state = await ExecuteAsync("ls");
        Assert.False(state.IsError);
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
