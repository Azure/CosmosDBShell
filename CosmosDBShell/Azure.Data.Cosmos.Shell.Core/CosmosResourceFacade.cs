// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;
using System.Runtime.CompilerServices;
using Azure.Data.Cosmos.Shell.States;
using global::Azure;
using global::Azure.ResourceManager.CosmosDB.Models;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

/// <summary>
/// Hybrid resource access for databases and containers. Prefers Azure Resource Manager
/// when an <see cref="ArmCosmosContext"/> is attached to the connected state, and otherwise
/// falls back to the Cosmos data plane. The fallback is required for connections that have
/// no ARM equivalent (account key, static token, emulator).
/// </summary>
internal static class CosmosResourceFacade
{
    public static async IAsyncEnumerable<string> GetDatabaseNamesAsync(ConnectedState state, [EnumeratorCancellation] CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            await foreach (var database in CosmosArmResourceProvider.GetDatabasesAsync(arm, token))
            {
                token.ThrowIfCancellationRequested();
                yield return database.Data.Resource.DatabaseName;
            }

            yield break;
        }

        using var iterator = state.Client.GetDatabaseQueryIterator<DatabaseProperties>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(token);
            foreach (var database in page)
            {
                yield return database.Id;
            }
        }
    }

    public static async IAsyncEnumerable<string> GetContainerNamesAsync(ConnectedState state, string databaseName, [EnumeratorCancellation] CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            await foreach (var container in CosmosArmResourceProvider.GetContainersAsync(arm, databaseName, token))
            {
                token.ThrowIfCancellationRequested();
                yield return container.Data.Resource.ContainerName;
            }

            yield break;
        }

        var database = state.Client.GetDatabase(databaseName);
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

    public static async Task<bool> DatabaseExistsAsync(ConnectedState state, string databaseName, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            try
            {
                await CosmosArmResourceProvider.GetDatabaseAsync(arm, databaseName, token);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        try
        {
            var response = await state.Client.GetDatabase(databaseName).ReadAsync(cancellationToken: token);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public static async Task<bool> ContainerExistsAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            try
            {
                await CosmosArmResourceProvider.GetContainerAsync(arm, databaseName, containerName, token);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        try
        {
            var response = await state.Client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public static async Task<string> CreateDatabaseAsync(ConnectedState state, string databaseName, string? scale, int? maxRu, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            var resource = await CosmosArmResourceProvider.CreateDatabaseAsync(arm, databaseName, scale, maxRu, token);
            return resource.Data.Resource.DatabaseName;
        }

        var throughput = CreateThroughputProperties(scale, maxRu);
        var response = await state.Client.CreateDatabaseIfNotExistsAsync(databaseName, throughput, cancellationToken: token);
        return response.Database.Id;
    }

    public static async Task<string> CreateContainerAsync(
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
        if (state.ArmContext is { } arm)
        {
            var resource = await CosmosArmResourceProvider.CreateContainerAsync(arm, databaseName, containerName, partitionKeyPaths, uniqueKey, indexPolicyJson, scale, maxRu, token);
            return resource.Data.Resource.ContainerName;
        }

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
            var policy = JsonConvert.DeserializeObject<IndexingPolicy>(indexPolicyJson)
                ?? throw new InvalidOperationException("Unable to parse indexing policy JSON.");
            props.IndexingPolicy = policy;
        }

        var throughput = CreateThroughputProperties(scale, maxRu);
        var database = state.Client.GetDatabase(databaseName);
        var response = await database.CreateContainerIfNotExistsAsync(props, throughput, cancellationToken: token);
        return response.Container.Id;
    }

    public static async Task DeleteDatabaseAsync(ConnectedState state, string databaseName, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            await CosmosArmResourceProvider.DeleteDatabaseAsync(arm, databaseName, token);
            return;
        }

        await state.Client.GetDatabase(databaseName).DeleteAsync(cancellationToken: token);
    }

    public static async Task DeleteContainerAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            await CosmosArmResourceProvider.DeleteContainerAsync(arm, databaseName, containerName, token);
            return;
        }

        await state.Client.GetDatabase(databaseName).GetContainer(containerName).DeleteContainerAsync(cancellationToken: token);
    }

    public static async Task<IReadOnlyList<string>> GetPartitionKeyPathsAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            var resource = await CosmosArmResourceProvider.GetContainerAsync(arm, databaseName, containerName, token);
            return CosmosArmResourceProvider.GetPartitionKeyPaths(resource);
        }

        var response = await state.Client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
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

    public static async Task<ContainerSettingsView> GetContainerSettingsAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            var resource = await CosmosArmResourceProvider.GetContainerAsync(arm, databaseName, containerName, token);
            int? min = null;
            int? max = null;
            ThroughputAvailability throughputAvailability = ThroughputAvailability.Available;
            string? throughputError = null;
            try
            {
                var throughputResponse = await resource.GetCosmosDBSqlContainerThroughputSetting().GetAsync(token);
                var throughput = throughputResponse.Value.Data.Resource;
                min = int.TryParse(throughput.MinimumThroughput, out var parsedMin) ? parsedMin : null;
                max = throughput.AutoscaleSettings?.MaxThroughput ?? throughput.Throughput ?? min;
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
            {
                throughputAvailability = ThroughputAvailability.NotConfigured;
            }
            catch (Exception ex)
            {
                throughputAvailability = ThroughputAvailability.Unavailable;
                throughputError = ex.Message;
            }

            var armResource = resource.Data.Resource;
            return new ContainerSettingsView(
                armResource.ContainerName,
                armResource.PartitionKey?.Paths?.ToArray() ?? [],
                armResource.AnalyticalStorageTtl,
                min,
                max,
                throughputAvailability,
                throughputError,
                GeospatialType: null,
                FullTextPolicy: null);
        }

        var dpResponse = await state.Client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
        var properties = dpResponse.Resource;
        int? dpMin = null;
        int? dpMax = null;
        ThroughputAvailability dpAvailability = ThroughputAvailability.Available;
        string? dpError = null;
        try
        {
            var throughputResponse = await state.Client.GetDatabase(databaseName).GetContainer(containerName).ReadThroughputAsync(new RequestOptions(), token);
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

    public static async Task<string> GetIndexingPolicyJsonAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            var resource = await CosmosArmResourceProvider.GetContainerAsync(arm, databaseName, containerName, token);
            var indexingPolicy = resource.Data.Resource.IndexingPolicy
                ?? throw new InvalidOperationException("Container has no indexing policy.");
            return CosmosArmResourceProvider.WriteArmModel(indexingPolicy);
        }

        var response = await state.Client.GetDatabase(databaseName).GetContainer(containerName).ReadContainerAsync(cancellationToken: token);
        var policy = response.Resource?.IndexingPolicy
            ?? throw new InvalidOperationException("Container has no indexing policy.");
        return JsonConvert.SerializeObject(policy);
    }

    public static async Task<string> ReplaceIndexingPolicyAsync(ConnectedState state, string databaseName, string containerName, string indexPolicyJson, CancellationToken token)
    {
        if (state.ArmContext is { } arm)
        {
            var resource = await CosmosArmResourceProvider.GetContainerAsync(arm, databaseName, containerName, token);
            var indexingPolicy = CosmosArmResourceProvider.ReadArmModel<CosmosDBIndexingPolicy>(indexPolicyJson);
            var data = resource.Data.Resource;
            data.IndexingPolicy = indexingPolicy;
            var content = new CosmosDBSqlContainerCreateOrUpdateContent(resource.Data.Location, data);
            var response = await resource.UpdateAsync(WaitUntil.Completed, content, token);
            var updated = response.Value.Data.Resource.IndexingPolicy;
            return CosmosArmResourceProvider.WriteArmModel(updated);
        }

        var container = state.Client.GetDatabase(databaseName).GetContainer(containerName);
        var current = await container.ReadContainerAsync(cancellationToken: token);
        var props = current.Resource;
        var policy = JsonConvert.DeserializeObject<IndexingPolicy>(indexPolicyJson)
            ?? throw new InvalidOperationException("Unable to parse indexing policy JSON.");
        props.IndexingPolicy = policy;
        var replaced = await container.ReplaceContainerAsync(props, cancellationToken: token);
        return JsonConvert.SerializeObject(replaced.Resource?.IndexingPolicy ?? policy);
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
