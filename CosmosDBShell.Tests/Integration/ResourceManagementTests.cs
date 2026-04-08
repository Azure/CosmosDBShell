// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Net.Http;
using System.Threading;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

using Microsoft.Azure.Cosmos;

using Xunit;
using Xunit.Sdk;

[Trait("Category", "Emulator")]
[Collection("Emulator")]
public class ResourceManagementTests : IAsyncLifetime
{
    private ShellInterpreter shell = null!;
    private CosmosClient? cosmosClient;
    private readonly List<string> createdDatabases = [];

    public async ValueTask InitializeAsync()
    {
        var envVar = Environment.GetEnvironmentVariable("COSMOS_EMULATOR_AVAILABLE");
        bool available;
        if (string.Equals(envVar, "true", StringComparison.OrdinalIgnoreCase))
        {
            available = true;
        }
        else
        {
            available = await ProbeEmulatorAsync();
        }

        if (!available)
        {
            throw SkipException.ForSkip("Cosmos DB emulator not available");
        }

        shell = ShellInterpreter.CreateInstance();

        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorTestBase.EmulatorEndpoint);
        await shell.ConnectAsync(connectionString, null);

        var options = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            ServerCertificateCustomValidationCallback = (_, _, _) => true,
        };
        cosmosClient = new CosmosClient(connectionString, options);
    }

    public async ValueTask DisposeAsync()
    {
        if (cosmosClient != null)
        {
            foreach (var db in createdDatabases)
            {
                try
                {
                    await cosmosClient.GetDatabase(db).DeleteAsync();
                }
                catch
                {
                    // Best-effort cleanup
                }
            }

            cosmosClient.Dispose();
        }

        shell?.Dispose();
    }

    [Fact]
    public async Task MkDb_CreatesDatabase_LsShowsIt()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        createdDatabases.Add(dbName);

        var state = await ExecuteAsync($"mkdb {dbName}");
        Assert.False(state.IsError);

        // Verify via Cosmos SDK that the database exists
        var dbResponse = await cosmosClient!.GetDatabase(dbName).ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, dbResponse.StatusCode);

        // Verify via shell ls
        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError);
    }

    [Fact]
    public async Task MkCon_CreatesContainer_LsShowsIt()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        createdDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");

        var state = await ExecuteAsync("mkcon TestCon /id");
        Assert.False(state.IsError);

        // Verify via Cosmos SDK that the container exists
        var conResponse = await cosmosClient!.GetContainer(dbName, "TestCon").ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, conResponse.StatusCode);

        // Verify via shell ls
        var lsState = await ExecuteAsync("ls");
        Assert.False(lsState.IsError);
    }

    [Fact]
    public async Task RmCon_RemovesContainer()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        createdDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon TempCon /id");

        var state = await ExecuteAsync("rmcon TempCon true");
        var errorMsg1 = state is Azure.Data.Cosmos.Shell.Core.ErrorCommandState err1 ? err1.Exception.ToString() : "not an error";
        Assert.False(state.IsError, errorMsg1);

        // Verify via Cosmos SDK that the container no longer exists
        var ex = await Assert.ThrowsAsync<CosmosException>(
            () => cosmosClient!.GetContainer(dbName, "TempCon").ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task RmDb_RemovesDatabase()
    {
        var dbName = $"RmTest_{Guid.NewGuid():N}";
        // Don't add to createdDatabases since we're deleting it in the test

        await ExecuteAsync($"mkdb {dbName}");

        var state = await ExecuteAsync($"rmdb {dbName} true");
        var errorMsg2 = state is Azure.Data.Cosmos.Shell.Core.ErrorCommandState err2 ? err2.Exception.ToString() : "not an error";
        Assert.False(state.IsError, errorMsg2);

        // Verify via Cosmos SDK that the database no longer exists
        var ex = await Assert.ThrowsAsync<CosmosException>(
            () => cosmosClient!.GetDatabase(dbName).ReadAsync(cancellationToken: TestContext.Current.CancellationToken));
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
        createdDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");

        // Missing leading slash in partition key
        var state = await ExecuteAsync("mkcon BadCon badkey");
        Assert.True(state.IsError);
    }

    private async Task<CommandState> ExecuteAsync(string command)
    {
        return await shell.ExecuteCommandAsync(command, CancellationToken.None);
    }

    private static async Task<bool> ProbeEmulatorAsync()
    {
        try
        {
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(new Uri($"{EmulatorTestBase.EmulatorEndpoint}/"));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
