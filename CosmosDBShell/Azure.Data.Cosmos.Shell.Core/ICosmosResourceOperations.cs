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

    Task<ContainerTtlView> GetTimeToLiveAsync(string databaseName, string containerName, CancellationToken token);

    Task<ContainerTtlView> ReplaceTimeToLiveAsync(string databaseName, string containerName, int? defaultTimeToLive, CancellationToken token);

    Task<ContainerAnalyticalTtlView> GetAnalyticalTimeToLiveAsync(string databaseName, string containerName, CancellationToken token);

    Task<ContainerAnalyticalTtlView> ReplaceAnalyticalTimeToLiveAsync(string databaseName, string containerName, int? analyticalTimeToLive, CancellationToken token);

    Task<ConflictResolutionView> GetConflictResolutionPolicyAsync(string databaseName, string containerName, CancellationToken token);

    Task<ConflictResolutionView> ReplaceConflictResolutionPolicyAsync(string databaseName, string containerName, ConflictResolutionUpdate update, CancellationToken token);

    Task<ThroughputView> GetThroughputAsync(string databaseName, string? containerName, CancellationToken token);

    Task<ThroughputView> ReplaceThroughputAsync(string databaseName, string? containerName, ThroughputUpdate update, CancellationToken token);
}