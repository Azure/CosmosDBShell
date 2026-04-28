// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

using Microsoft.Azure.Cosmos;

using Xunit;

/// <summary>
/// Base class for emulator integration tests that need a shell connected to the local emulator
/// and a side-channel <see cref="CosmosClient"/> for SDK-level verification and cleanup.
/// Use this instead of hand-rolling <c>IAsyncLifetime</c> + probe + connect logic.
/// </summary>
public abstract class ConnectedEmulatorTestBase : EmulatorTestBase
{
    private CosmosClient? cosmosClient;

    /// <summary>
    /// Gets the SDK client connected to the local emulator. Tests can use this to create or
    /// verify resources outside of the shell.
    /// </summary>
    internal CosmosClient CosmosClient => cosmosClient ?? throw new InvalidOperationException("Cosmos client is not initialized");

    /// <summary>
    /// Gets the list of databases created by the test. Any databases added here are deleted
    /// via the side-channel client during <see cref="DisposeAsync"/>.
    /// </summary>
    internal List<string> CreatedDatabases { get; } = [];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        await Shell.ConnectAsync(connectionString, null);

        var options = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            ServerCertificateCustomValidationCallback = (_, _, _) => true,
        };
        cosmosClient = new CosmosClient(connectionString, options);
    }

    public override async ValueTask DisposeAsync()
    {
        if (cosmosClient != null)
        {
            foreach (var db in CreatedDatabases)
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
            cosmosClient = null;
        }

        await base.DisposeAsync();
    }

    internal Task<CommandState> ExecuteAsync(string command)
    {
        return Shell.ExecuteCommandAsync(command, TestContext.Current.CancellationToken);
    }
}
