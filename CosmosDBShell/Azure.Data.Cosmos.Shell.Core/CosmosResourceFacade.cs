// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.States;

/// <summary>
/// Resource access facade for database and container operations. Commands use this
/// API without caring whether the current connection is backed by Azure Resource
/// Manager or the Cosmos DB data plane.
/// </summary>
internal static class CosmosResourceFacade
{
    public static IAsyncEnumerable<string> GetDatabaseNamesAsync(ConnectedState state, CancellationToken token)
    {
        return For(state).GetDatabaseNamesAsync(token);
    }

    public static IAsyncEnumerable<string> GetContainerNamesAsync(ConnectedState state, string databaseName, CancellationToken token)
    {
        return For(state).GetContainerNamesAsync(databaseName, token);
    }

    public static Task<bool> DatabaseExistsAsync(ConnectedState state, string databaseName, CancellationToken token)
    {
        return For(state).DatabaseExistsAsync(databaseName, token);
    }

    public static Task<bool> ContainerExistsAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        return For(state).ContainerExistsAsync(databaseName, containerName, token);
    }

    public static Task<string> CreateDatabaseAsync(ConnectedState state, string databaseName, string? scale, int? maxRu, CancellationToken token)
    {
        return For(state).CreateDatabaseAsync(databaseName, scale, maxRu, token);
    }

    public static Task<string> CreateContainerAsync(
        ConnectedState state,
        string databaseName,
        string containerName,
        IReadOnlyList<string> partitionKeyPaths,
        string? uniqueKey,
        string? indexPolicyJson,
        string? scale,
        int? maxRu,
        CancellationToken token)
    {
        return For(state).CreateContainerAsync(databaseName, containerName, partitionKeyPaths, uniqueKey, indexPolicyJson, scale, maxRu, token);
    }

    public static Task DeleteDatabaseAsync(ConnectedState state, string databaseName, CancellationToken token)
    {
        return For(state).DeleteDatabaseAsync(databaseName, token);
    }

    public static Task DeleteContainerAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        return For(state).DeleteContainerAsync(databaseName, containerName, token);
    }

    public static Task<IReadOnlyList<string>> GetPartitionKeyPathsAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        return For(state).GetPartitionKeyPathsAsync(databaseName, containerName, token);
    }

    public static Task<ContainerSettingsView> GetContainerSettingsAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        return For(state).GetContainerSettingsAsync(databaseName, containerName, token);
    }

    public static Task<string> GetIndexingPolicyJsonAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        return For(state).GetIndexingPolicyJsonAsync(databaseName, containerName, token);
    }

    public static Task<string> ReplaceIndexingPolicyAsync(ConnectedState state, string databaseName, string containerName, string indexPolicyJson, CancellationToken token)
    {
        return For(state).ReplaceIndexingPolicyAsync(databaseName, containerName, indexPolicyJson, token);
    }

    private static ICosmosResourceOperations For(ConnectedState state)
    {
        return state.ArmContext is { } armContext
            ? new ArmCosmosResourceOperations(armContext)
            : new DataPlaneCosmosResourceOperations(state.Client);
    }
}