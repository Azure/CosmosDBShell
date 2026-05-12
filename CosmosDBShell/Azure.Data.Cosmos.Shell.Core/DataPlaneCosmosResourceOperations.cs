// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

internal sealed class DataPlaneCosmosResourceOperations(CosmosClient client) : ICosmosResourceOperations
{
    public async IAsyncEnumerable<string> GetDatabaseNamesAsync([EnumeratorCancellation] CancellationToken token)
    {
        using var iterator = client.GetDatabaseQueryIterator<DatabaseProperties>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(token);
            foreach (var database in page)
            {
                yield return database.Id;
            }
        }
    }

    public async IAsyncEnumerable<string> GetContainerNamesAsync(string databaseName, [EnumeratorCancellation] CancellationToken token)
    {
        var database = client.GetDatabase(databaseName);
        using var iterator = database.GetContainerQueryIterator<ContainerProperties>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(token);
            foreach (var container in page)
            {
                yield return container.Id;
            }
        }
    }

    public async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken token)
    {
        try
        {
            var response = await client.GetDatabase(databaseName).ReadAsync(cancellationToken: token);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> ContainerExistsAsync(string databaseName, string containerName, CancellationToken token)
    {
        try
        {
            var response = await client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<string> CreateDatabaseAsync(string databaseName, string? scale, int? maxRu, CancellationToken token)
    {
        var throughput = CreateThroughputProperties(scale, maxRu);
        var response = await client.CreateDatabaseIfNotExistsAsync(databaseName, throughput, cancellationToken: token);
        return response.Database.Id;
    }

    public async Task<string> CreateContainerAsync(
        string databaseName,
        string containerName,
        IReadOnlyList<string> partitionKeyPaths,
        string? uniqueKey,
        string? indexPolicyJson,
        string? scale,
        int? maxRu,
        CancellationToken token)
    {
        var props = partitionKeyPaths.Count > 1
            ? new ContainerProperties(containerName, partitionKeyPaths.ToList())
            : new ContainerProperties(containerName, partitionKeyPaths[0]);

        if (!string.IsNullOrWhiteSpace(uniqueKey))
        {
            var key = new UniqueKey();
            foreach (var path in uniqueKey.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                key.Paths.Add(path);
            }

            props.UniqueKeyPolicy.UniqueKeys.Add(key);
        }

        if (!string.IsNullOrWhiteSpace(indexPolicyJson))
        {
            props.IndexingPolicy = ParseIndexingPolicy(indexPolicyJson);
        }

        var throughput = CreateThroughputProperties(scale, maxRu);
        var database = client.GetDatabase(databaseName);
        var response = await database.CreateContainerIfNotExistsAsync(props, throughput, cancellationToken: token);
        return response.Container.Id;
    }

    public Task DeleteDatabaseAsync(string databaseName, CancellationToken token)
    {
        return client.GetDatabase(databaseName).DeleteAsync(cancellationToken: token);
    }

    public Task DeleteContainerAsync(string databaseName, string containerName, CancellationToken token)
    {
        return client.GetDatabase(databaseName).GetContainer(containerName).DeleteContainerAsync(cancellationToken: token);
    }

    public async Task<IReadOnlyList<string>> GetPartitionKeyPathsAsync(string databaseName, string containerName, CancellationToken token)
    {
        var response = await client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
        var properties = response.Resource;
        if (properties == null)
        {
            return [];
        }

        if (properties.PartitionKeyPaths is { Count: > 0 } paths)
        {
            return [.. paths];
        }

        return string.IsNullOrEmpty(properties.PartitionKeyPath) ? [] : [properties.PartitionKeyPath];
    }

    public async Task<ContainerSettingsView> GetContainerSettingsAsync(string databaseName, string containerName, CancellationToken token)
    {
        var dpResponse = await client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
        var properties = dpResponse.Resource;
        int? dpMin = null;
        int? dpMax = null;
        ThroughputAvailability dpAvailability = ThroughputAvailability.Available;
        string? dpError = null;
        try
        {
            var throughputResponse = await client.GetDatabase(databaseName).GetContainer(containerName).ReadThroughputAsync(new RequestOptions(), token);
            dpMin = throughputResponse.MinThroughput;
            dpMax = throughputResponse.Resource?.AutoscaleMaxThroughput ?? throughputResponse.Resource?.Throughput ?? dpMin;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            dpAvailability = ThroughputAvailability.NotConfigured;
        }
        catch (Exception ex)
        {
            dpAvailability = ThroughputAvailability.Unavailable;
            dpError = ex.Message;
        }

        string? geospatialType = properties.GeospatialConfig?.GeospatialType switch
        {
            GeospatialType.Geography => "Geography",
            GeospatialType.Geometry => "Geometry",
            _ => null,
        };

        ContainerFullTextPolicyView? fullTextView = null;
        if (properties.FullTextPolicy is { } fullTextPolicy)
        {
            var paths = fullTextPolicy.FullTextPaths is null
                ? Array.Empty<ContainerFullTextPathView>()
                : fullTextPolicy.FullTextPaths.Select(p => new ContainerFullTextPathView(p.Path, p.Language)).ToArray();
            fullTextView = new ContainerFullTextPolicyView(fullTextPolicy.DefaultLanguage, paths);
        }

        return new ContainerSettingsView(
            properties.Id,
            properties.PartitionKeyPaths?.ToArray() ?? (properties.PartitionKeyPath != null ? [properties.PartitionKeyPath] : []),
            properties.AnalyticalStoreTimeToLiveInSeconds,
            dpMin,
            dpMax,
            dpAvailability,
            dpError,
            geospatialType,
            fullTextView);
    }

    public async Task<string> GetIndexingPolicyJsonAsync(string databaseName, string containerName, CancellationToken token)
    {
        var response = await client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
        var policy = response.Resource?.IndexingPolicy
            ?? throw new IndexPolicyMissingException();
        return JsonConvert.SerializeObject(policy, Formatting.Indented);
    }

    public async Task<string> ReplaceIndexingPolicyAsync(string databaseName, string containerName, string indexPolicyJson, CancellationToken token)
    {
        var container = client.GetDatabase(databaseName).GetContainer(containerName);
        var current = await container.ReadContainerAsync(cancellationToken: token);
        var props = current.Resource;
        var policy = ParseIndexingPolicy(indexPolicyJson);
        props.IndexingPolicy = policy;
        var replaced = await container.ReplaceContainerAsync(props, cancellationToken: token);
        return JsonConvert.SerializeObject(replaced.Resource?.IndexingPolicy ?? policy, Formatting.Indented);
    }

    private static IndexingPolicy ParseIndexingPolicy(string indexPolicyJson)
    {
        try
        {
            return JsonConvert.DeserializeObject<IndexingPolicy>(indexPolicyJson)
                ?? throw new InvalidIndexingPolicyJsonException();
        }
        catch (JsonException ex)
        {
            throw new InvalidIndexingPolicyJsonException(ex);
        }
    }

    private static ThroughputProperties CreateThroughputProperties(string? scale, int? maxRu)
    {
        var ru = maxRu ?? 1000;
        if (string.Equals(scale, "manual", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scale, "m", StringComparison.OrdinalIgnoreCase))
        {
            return ThroughputProperties.CreateManualThroughput(ru);
        }

        return ThroughputProperties.CreateAutoscaleThroughput(ru);
    }
}