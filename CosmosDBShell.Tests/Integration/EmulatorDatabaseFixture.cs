// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

using Microsoft.Azure.Cosmos;

using Xunit;

public class EmulatorDatabaseFixture : IAsyncLifetime
{
    private CosmosClient? cosmosClient;

    public ShellInterpreter Shell { get; private set; } = null!;

    public string DatabaseName { get; private set; } = null!;

    public string ContainerName { get; } = "TestContainer";

    public bool IsAvailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        IsAvailable = await EmulatorProbe.IsAvailableAsync();
        if (!IsAvailable)
        {
            return;
        }

        Shell = ShellInterpreter.CreateInstance();
        DatabaseName = $"IntTest_{Guid.NewGuid():N}";

        // Connect using the shell's ConnectAsync directly (bypasses argument parsing)
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorTestBase.EmulatorEndpoint);
        await Shell.ConnectAsync(connectionString, null);

        // Create test database and container via SDK for reliability
        var options = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            ServerCertificateCustomValidationCallback = (_, _, _) => true,
        };
        cosmosClient = new CosmosClient(connectionString, options);

        var dbResponse = await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseName);
        await dbResponse.Database.CreateContainerIfNotExistsAsync(ContainerName, "/id");

        // Navigate to the database so tests can use ls, cd, etc.
        await Shell.ExecuteCommandAsync($"cd {DatabaseName}", TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (cosmosClient != null)
        {
            try
            {
                await cosmosClient.GetDatabase(DatabaseName).DeleteAsync();
            }
            catch
            {
                // Best-effort cleanup
            }

            cosmosClient.Dispose();
        }

        Shell?.Dispose();
    }
}
