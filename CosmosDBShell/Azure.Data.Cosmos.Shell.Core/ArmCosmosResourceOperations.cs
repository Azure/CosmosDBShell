// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;
using System.Runtime.CompilerServices;
using global::Azure;
using global::Azure.ResourceManager.CosmosDB.Models;

internal sealed class ArmCosmosResourceOperations(ArmCosmosContext context) : ICosmosResourceOperations
{
    public async IAsyncEnumerable<string> GetDatabaseNamesAsync([EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var database in CosmosArmResourceProvider.GetDatabasesAsync(context, token))
        {
            token.ThrowIfCancellationRequested();
            yield return database.Data.Resource.DatabaseName;
        }
    }

    public async IAsyncEnumerable<string> GetContainerNamesAsync(string databaseName, [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var container in CosmosArmResourceProvider.GetContainersAsync(context, databaseName, token))
        {
            token.ThrowIfCancellationRequested();
            yield return container.Data.Resource.ContainerName;
        }
    }

    public async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken token)
    {
        try
        {
            await CosmosArmResourceProvider.GetDatabaseAsync(context, databaseName, token);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> ContainerExistsAsync(string databaseName, string containerName, CancellationToken token)
    {
        try
        {
            await CosmosArmResourceProvider.GetContainerAsync(context, databaseName, containerName, token);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<string> CreateDatabaseAsync(string databaseName, string? scale, int? maxRu, CancellationToken token)
    {
        var resource = await CosmosArmResourceProvider.CreateDatabaseAsync(context, databaseName, scale, maxRu, token);
        return resource.Data.Resource.DatabaseName;
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
        CosmosDBIndexingPolicy? parsedIndex = null;
        if (!string.IsNullOrWhiteSpace(indexPolicyJson))
        {
            try
            {
                parsedIndex = CosmosArmResourceProvider.ReadArmModel<CosmosDBIndexingPolicy>(indexPolicyJson);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidIndexingPolicyJsonException(ex);
            }
        }

        var resource = await CosmosArmResourceProvider.CreateContainerAsync(context, databaseName, containerName, partitionKeyPaths, uniqueKey, parsedIndex, scale, maxRu, token);
        return resource.Data.Resource.ContainerName;
    }

    public Task DeleteDatabaseAsync(string databaseName, CancellationToken token)
    {
        return CosmosArmResourceProvider.DeleteDatabaseAsync(context, databaseName, token);
    }

    public Task DeleteContainerAsync(string databaseName, string containerName, CancellationToken token)
    {
        return CosmosArmResourceProvider.DeleteContainerAsync(context, databaseName, containerName, token);
    }

    public async Task<IReadOnlyList<string>> GetPartitionKeyPathsAsync(string databaseName, string containerName, CancellationToken token)
    {
        var resource = await CosmosArmResourceProvider.GetContainerAsync(context, databaseName, containerName, token);
        return CosmosArmResourceProvider.GetPartitionKeyPaths(resource);
    }

    public async Task<ContainerSettingsView> GetContainerSettingsAsync(string databaseName, string containerName, CancellationToken token)
    {
        var resource = await CosmosArmResourceProvider.GetContainerAsync(context, databaseName, containerName, token);
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

    public async Task<string> GetIndexingPolicyJsonAsync(string databaseName, string containerName, CancellationToken token)
    {
        var resource = await CosmosArmResourceProvider.GetContainerAsync(context, databaseName, containerName, token);
        var indexingPolicy = resource.Data.Resource.IndexingPolicy
            ?? throw new IndexPolicyMissingException();
        return CosmosResourceJson.IndentJson(CosmosArmResourceProvider.WriteArmModel(indexingPolicy));
    }

    public async Task<string> ReplaceIndexingPolicyAsync(string databaseName, string containerName, string indexPolicyJson, CancellationToken token)
    {
        var resource = await CosmosArmResourceProvider.GetContainerAsync(context, databaseName, containerName, token);
        CosmosDBIndexingPolicy indexingPolicy;
        try
        {
            indexingPolicy = CosmosArmResourceProvider.ReadArmModel<CosmosDBIndexingPolicy>(indexPolicyJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidIndexingPolicyJsonException(ex);
        }

        var data = resource.Data.Resource;
        data.IndexingPolicy = indexingPolicy;
        var content = new CosmosDBSqlContainerCreateOrUpdateContent(resource.Data.Location, data);
        var response = await resource.UpdateAsync(WaitUntil.Completed, content, token);
        var updated = response.Value.Data.Resource.IndexingPolicy;
        return CosmosResourceJson.IndentJson(CosmosArmResourceProvider.WriteArmModel(updated));
    }

    public async Task<ThroughputView> GetThroughputAsync(string databaseName, string? containerName, CancellationToken token)
    {
        bool isContainer = !string.IsNullOrEmpty(containerName);
        string scope = isContainer ? "container" : "database";
        string resourceName = isContainer ? containerName! : databaseName;
        try
        {
            ThroughputSettingsResourceInfo info;
            if (!isContainer)
            {
                var database = await CosmosArmResourceProvider.GetDatabaseAsync(context, databaseName, token);
                var response = await database.GetCosmosDBSqlDatabaseThroughputSetting().GetAsync(token);
                info = response.Value.Data.Resource;
            }
            else
            {
                var container = await CosmosArmResourceProvider.GetContainerAsync(context, databaseName, containerName, token);
                var response = await container.GetCosmosDBSqlContainerThroughputSetting().GetAsync(token);
                info = response.Value.Data.Resource;
            }

            return BuildThroughputView(scope, resourceName, info);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return new ThroughputView(scope, resourceName, false, null, null, null, ThroughputAvailability.NotConfigured, null);
        }
    }

    public async Task<ThroughputView> ReplaceThroughputAsync(string databaseName, string? containerName, ThroughputUpdate update, CancellationToken token)
    {
        bool isContainer = !string.IsNullOrEmpty(containerName);
        string scope = isContainer ? "container" : "database";
        string resourceName = isContainer ? containerName! : databaseName;
        var resourceInfo = update.IsAutoscale
            ? new ThroughputSettingsResourceInfo { AutoscaleSettings = new AutoscaleSettingsResourceInfo(update.Throughput) }
            : new ThroughputSettingsResourceInfo { Throughput = update.Throughput };
        var data = new ThroughputSettingsUpdateData(context.Account.Data.Location, resourceInfo);
        try
        {
            ThroughputSettingsResourceInfo info;
            if (!isContainer)
            {
                var database = await CosmosArmResourceProvider.GetDatabaseAsync(context, databaseName, token);
                var setting = database.GetCosmosDBSqlDatabaseThroughputSetting();
                var current = await setting.GetAsync(token);
                if (IsAutoscale(current.Value.Data.Resource) != update.IsAutoscale)
                {
                    if (update.IsAutoscale)
                    {
                        await setting.MigrateSqlDatabaseToAutoscaleAsync(WaitUntil.Completed, token);
                    }
                    else
                    {
                        await setting.MigrateSqlDatabaseToManualThroughputAsync(WaitUntil.Completed, token);
                    }
                }

                var operation = await setting.CreateOrUpdateAsync(WaitUntil.Completed, data, token);
                info = operation.Value.Data.Resource;
            }
            else
            {
                var container = await CosmosArmResourceProvider.GetContainerAsync(context, databaseName, containerName, token);
                var setting = container.GetCosmosDBSqlContainerThroughputSetting();
                var current = await setting.GetAsync(token);
                if (IsAutoscale(current.Value.Data.Resource) != update.IsAutoscale)
                {
                    if (update.IsAutoscale)
                    {
                        await setting.MigrateSqlContainerToAutoscaleAsync(WaitUntil.Completed, token);
                    }
                    else
                    {
                        await setting.MigrateSqlContainerToManualThroughputAsync(WaitUntil.Completed, token);
                    }
                }

                var operation = await setting.CreateOrUpdateAsync(WaitUntil.Completed, data, token);
                info = operation.Value.Data.Resource;
            }

            return BuildThroughputView(scope, resourceName, info);
        }
        catch (RequestFailedException ex) when (ex.Status is (int)HttpStatusCode.NotFound or (int)HttpStatusCode.BadRequest)
        {
            throw new ThroughputNotConfiguredException(resourceName, ex);
        }
    }

    private static bool IsAutoscale(ThroughputSettingsResourceInfo info) => info.AutoscaleSettings?.MaxThroughput != null;

    private static ThroughputView BuildThroughputView(string scope, string resourceName, ThroughputSettingsResourceInfo info)
    {
        int? min = int.TryParse(info.MinimumThroughput, out var parsedMin) ? parsedMin : null;
        int? autoscaleMax = info.AutoscaleSettings?.MaxThroughput;
        bool isAutoscale = autoscaleMax.HasValue;
        return new ThroughputView(
            scope,
            resourceName,
            isAutoscale,
            info.Throughput,
            autoscaleMax,
            min,
            ThroughputAvailability.Available,
            null);
    }
}