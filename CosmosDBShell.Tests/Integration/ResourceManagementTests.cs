// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Microsoft.Azure.Cosmos;

using Xunit;

public class ResourceManagementTests : ConnectedEmulatorTestBase
{
    [Fact]
    public async Task MkDb_CreatesDatabase_LsShowsIt()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        var state = await ExecuteAsync($"mkdb {dbName}");
        Assert.False(state.IsError);

        // Verify via Cosmos SDK that the database exists
        var dbResponse = await CosmosClient.GetDatabase(dbName).ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, dbResponse.StatusCode);

        // Verify via shell ls
        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError);
    }

    [Fact]
    public async Task MkCon_CreatesContainer_LsShowsIt()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");

        var state = await ExecuteAsync("mkcon TestCon /id");
        Assert.False(state.IsError);

        // Verify via Cosmos SDK that the container exists
        var conResponse = await CosmosClient.GetContainer(dbName, "TestCon").ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, conResponse.StatusCode);

        // Verify via shell ls
        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError);
    }

    [Fact]
    public async Task RmCon_RemovesContainer()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon TempCon /id");

        var state = await ExecuteAsync("rmcon TempCon true");
        Assert.False(state.IsError, FormatError(state));

        // Verify via Cosmos SDK that the container no longer exists
        var ex = await Assert.ThrowsAsync<CosmosException>(
            () => CosmosClient.GetContainer(dbName, "TempCon").ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task RmDb_RemovesDatabase()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";

        // Don't add to CreatedDatabases since we're deleting it in the test
        await ExecuteAsync($"mkdb {dbName}");

        var state = await ExecuteAsync($"rmdb {dbName} true");
        Assert.False(state.IsError, FormatError(state));

        // Verify via Cosmos SDK that the database no longer exists
        var ex = await Assert.ThrowsAsync<CosmosException>(
            () => CosmosClient.GetDatabase(dbName).ReadAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task MkDb_InvalidName_ReturnsError()
    {
        // Cosmos DB rejects names with certain characters
        var state = await ExecuteAsync("mkdb \"invalid/db\\name\"");
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task MkCon_InvalidPartitionKey_ReturnsError()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");

        // Missing leading slash in partition key
        var state = await ExecuteAsync("mkcon BadCon badkey");
        Assert.True(state.IsError);
    }

    [Fact]
    public async Task Create_Database_CreatesDatabase()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        var state = await ExecuteAsync($"create database {dbName}");
        Assert.False(state.IsError);

        var dbResponse = await CosmosClient.GetDatabase(dbName).ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, dbResponse.StatusCode);
    }

    [Fact]
    public async Task Create_Database_MissingName_ReturnsError()
    {
        var state = await ExecuteAsync("create database");
        Assert.True(state.IsError);
    }
}
