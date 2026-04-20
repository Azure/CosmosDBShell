// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;
using System.Threading;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

using Microsoft.Azure.Cosmos;

using Xunit;

[Trait("Category", "Emulator")]
[Collection("Emulator")]
public class EndToEndScriptTests : IAsyncLifetime
{
    private ShellInterpreter shell = null!;
    private CosmosClient? cosmosClient;
    private readonly List<string> createdDatabases = [];

    public async ValueTask InitializeAsync()
    {
        await EmulatorProbe.EnsureAvailableAsync();

        shell = ShellInterpreter.CreateInstance();

        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorTestBase.EmulatorEndpoint);
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
    public async Task Script_CreateDbContainerAndInsertItems()
    {
        var dbName = $"E2E_{Guid.NewGuid():N}";
        createdDatabases.Add(dbName);

        // Connect via SDK API
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorTestBase.EmulatorEndpoint);
        await shell.ConnectAsync(connectionString, null);

        var script = $"""
            mkdb {dbName}
            cd {dbName}
            mkcon Items /id
            cd Items
            mkitem '{JsonSerializer.Serialize(new { id = "item1", name = "first" })}'
            mkitem '{JsonSerializer.Serialize(new { id = "item2", name = "second" })}'
            mkitem '{JsonSerializer.Serialize(new { id = "item3", name = "third" })}'
            ls
            """;

        var state = await ExecuteAsync(script);
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task Script_NavigateAndQuery()
    {
        var dbName = $"E2E_{Guid.NewGuid():N}";
        createdDatabases.Add(dbName);

        // Connect via SDK API
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorTestBase.EmulatorEndpoint);
        await shell.ConnectAsync(connectionString, null);

        // Setup
        var setupScript = $"""
            mkdb {dbName}
            cd {dbName}
            mkcon QueryTest /id
            cd QueryTest
            mkitem '{JsonSerializer.Serialize(new { id = "q1", val = 10 })}'
            mkitem '{JsonSerializer.Serialize(new { id = "q2", val = 20 })}'
            """;

        var setupState = await ExecuteAsync(setupScript);
        Assert.False(setupState.IsError);

        // Query
        var queryState = await ExecuteAsync("query \"SELECT * FROM c WHERE c.val > 5\"");
        var errorMsg = queryState is Azure.Data.Cosmos.Shell.Core.ErrorCommandState err ? err.Exception.ToString() : "not an error";
        Assert.False(queryState.IsError, errorMsg);
    }

    private async Task<CommandState> ExecuteAsync(string command)
    {
        return await shell.ExecuteCommandAsync(command, CancellationToken.None);
    }
}
