// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal interface ICosmosResourceOperations
{
    IAsyncEnumerable<string> GetDatabaseNamesAsync(CancellationToken token);

    IAsyncEnumerable<string> GetContainerNamesAsync(string databaseName, CancellationToken token);

    Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken token);

    Task<bool> ContainerExistsAsync(string databaseName, string containerName, CancellationToken token);

    Task<string> CreateDatabaseAsync(string databaseName, string? scale, int? maxRu, CancellationToken token);

    Task<string> CreateContainerAsync(
        string databaseName,
        string containerName,
        IReadOnlyList<string> partitionKeyPaths,
        string? uniqueKey,
        string? indexPolicyJson,
        string? scale,
        int? maxRu,
        CancellationToken token);

    Task DeleteDatabaseAsync(string databaseName, CancellationToken token);

    Task DeleteContainerAsync(string databaseName, string containerName, CancellationToken token);

    Task<IReadOnlyList<string>> GetPartitionKeyPathsAsync(string databaseName, string containerName, CancellationToken token);

    Task<ContainerSettingsView> GetContainerSettingsAsync(string databaseName, string containerName, CancellationToken token);

    Task<string> GetIndexingPolicyJsonAsync(string databaseName, string containerName, CancellationToken token);

    Task<string> ReplaceIndexingPolicyAsync(string databaseName, string containerName, string indexPolicyJson, CancellationToken token);
}