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

    [Fact]
    public async Task Cd_RelativeNameFromContainer_ErrorsAndStaysInPlace()
    {
        // Repro for AzureCosmosDB/cosmosdb-shell-preview#26: from /db/container,
        // 'cd <name>' previously silently stayed in the current container instead
        // of raising an error. The path goes beyond the /database/container
        // hierarchy so cd must reject it.
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");

        var state = await ExecuteAsync("cd nonexistent-database");
        Assert.True(state.IsError, "cd should fail when a relative name from a container would exceed /database/container");

        var currentLocation = await GetCurrentLocationAsync();
        Assert.Contains(Fixture.DatabaseName, currentLocation);
        Assert.Contains(Fixture.ContainerName, currentLocation);
    }

    [Fact]
    public async Task Cd_DotDot_FromContainerReturnsToDatabase()
    {
        // Sanity check that the documented escape hatch ('cd ..' first) keeps
        // working after the relative-name fix.
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");

        var state = await ExecuteAsync("cd ..");
        Assert.False(state.IsError, FormatError(state));

        var currentLocation = await GetCurrentLocationAsync();
        Assert.Contains(Fixture.DatabaseName, currentLocation);
        Assert.DoesNotContain(Fixture.ContainerName, currentLocation);
    }

    [Fact]
    public async Task Cd_AbsolutePathFromContainer_NavigatesToDatabase()
    {
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");

        var state = await ExecuteAsync($"cd /{Fixture.DatabaseName}");
        Assert.False(state.IsError, FormatError(state));

        var currentLocation = await GetCurrentLocationAsync();
        Assert.Contains(Fixture.DatabaseName, currentLocation);
        Assert.DoesNotContain(Fixture.ContainerName, currentLocation);
    }

    [Fact]
    public async Task Cd_TooManySegmentsFromDatabase_Errors()
    {
        await NavigateToRootAsync();
        await ExecuteAsync($"cd {Fixture.DatabaseName}");

        var state = await ExecuteAsync("cd extra/too-deep");
        Assert.True(state.IsError, "cd should fail when a relative path from a database would exceed /database/container");
    }

    private async Task NavigateToRootAsync()
    {
        var state = await ExecuteAsync("cd");
        Assert.False(state.IsError, FormatError(state));
    }

    private async Task<string> GetCurrentLocationAsync()
    {
        var state = await ExecuteAsync("pwd");
        Assert.False(state.IsError, FormatError(state));

        var json = IntegrationTestBase.GetJson(state);
        return Assert.IsType<string>(json.GetProperty("currentLocation").GetString());
    }
}
