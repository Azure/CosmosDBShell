// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;
using System.Runtime.CompilerServices;
using Azure.Data.Cosmos.Shell.Util;
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
        var properties = GetContainerPropertiesOrThrow(dpResponse);
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
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ThroughputErrors.IsServerlessThroughputError(ex.Message))
        {
            dpAvailability = ThroughputAvailability.Serverless;
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

        ContainerIndexingPolicyView? indexingView = null;
        if (properties.IndexingPolicy is { } indexingPolicy)
        {
            indexingView = new ContainerIndexingPolicyView(
                indexingPolicy.IndexingMode.ToString(),
                indexingPolicy.Automatic,
                indexingPolicy.IncludedPaths?.Count ?? 0,
                indexingPolicy.ExcludedPaths?.Count ?? 0,
                indexingPolicy.CompositeIndexes?.Count ?? 0,
                indexingPolicy.SpatialIndexes?.Count ?? 0,
                indexingPolicy.VectorIndexes?.Count ?? 0);
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
            fullTextView,
            indexingView);
    }

    public async Task<string> GetIndexingPolicyJsonAsync(string databaseName, string containerName, CancellationToken token)
    {
        var response = await client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
        var properties = GetContainerPropertiesOrThrow(response);
        var policy = properties.IndexingPolicy
            ?? throw new IndexPolicyMissingException();
        return JsonConvert.SerializeObject(policy, Formatting.Indented);
    }

    public async Task<string> ReplaceIndexingPolicyAsync(string databaseName, string containerName, string indexPolicyJson, CancellationToken token)
    {
        var container = client.GetDatabase(databaseName).GetContainer(containerName);
        var current = await container.ReadContainerAsync(cancellationToken: token);
        var props = GetContainerPropertiesOrThrow(current);
        var policy = ParseIndexingPolicy(indexPolicyJson);
        props.IndexingPolicy = policy;
        var replaced = await container.ReplaceContainerAsync(props, cancellationToken: token);
        return JsonConvert.SerializeObject(replaced.Resource?.IndexingPolicy ?? policy, Formatting.Indented);
    }

    public async Task<ThroughputView> GetThroughputAsync(string databaseName, string? containerName, CancellationToken token)
    {
        bool isContainer = !string.IsNullOrEmpty(containerName);
        string scope = isContainer ? "container" : "database";
        string resourceName = isContainer ? containerName! : databaseName;
        try
        {
            var throughputResponse = !isContainer
                ? await client.GetDatabase(databaseName).ReadThroughputAsync(new RequestOptions(), token)
                : await client.GetDatabase(databaseName).GetContainer(containerName).ReadThroughputAsync(new RequestOptions(), token);
            return BuildThroughputView(scope, resourceName, throughputResponse);
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new ThroughputView(scope, resourceName, false, null, null, null, ThroughputAvailability.NotConfigured, null);
        }
    }

    public async Task<ThroughputView> ReplaceThroughputAsync(string databaseName, string? containerName, ThroughputUpdate update, CancellationToken token)
    {
        bool isContainer = !string.IsNullOrEmpty(containerName);
        string scope = isContainer ? "container" : "database";
        string resourceName = isContainer ? containerName! : databaseName;

        ThroughputResponse currentResponse;
        try
        {
            currentResponse = !isContainer
                ? await client.GetDatabase(databaseName).ReadThroughputAsync(new RequestOptions(), token)
                : await client.GetDatabase(databaseName).GetContainer(containerName).ReadThroughputAsync(new RequestOptions(), token);
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            throw new ThroughputNotConfiguredException(resourceName, ex);
        }

        // The data-plane SDK can only change the value within the current mode; it
        // cannot migrate between manual and autoscale. Detect that up front and fail
        // with a clear error instead of silently leaving the mode unchanged.
        bool currentIsAutoscale = currentResponse.Resource?.AutoscaleMaxThroughput is not null;
        if (currentIsAutoscale != update.IsAutoscale)
        {
            throw new ThroughputModeSwitchNotSupportedException(resourceName, update.IsAutoscale);
        }

        var properties = update.IsAutoscale
            ? ThroughputProperties.CreateAutoscaleThroughput(update.Throughput)
            : ThroughputProperties.CreateManualThroughput(update.Throughput);
        try
        {
            var throughputResponse = !isContainer
                ? await client.GetDatabase(databaseName).ReplaceThroughputAsync(properties, cancellationToken: token)
                : await client.GetDatabase(databaseName).GetContainer(containerName).ReplaceThroughputAsync(properties, cancellationToken: token);
            return BuildThroughputView(scope, resourceName, throughputResponse);
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            throw new ThroughputNotConfiguredException(resourceName, ex);
        }
    }

    private static ThroughputView BuildThroughputView(string scope, string resourceName, ThroughputResponse response)
    {
        var resource = response.Resource;
        int? autoscaleMax = resource?.AutoscaleMaxThroughput;
        bool isAutoscale = autoscaleMax.HasValue;
        return new ThroughputView(
            scope,
            resourceName,
            isAutoscale,
            resource?.Throughput,
            autoscaleMax,
            response.MinThroughput,
            ThroughputAvailability.Available,
            null);
    }

    private static ContainerProperties GetContainerPropertiesOrThrow(ContainerResponse response)
    {
        return response.Resource ?? throw new ShellException(MessageService.GetString("error-unable_to_read_container"));
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