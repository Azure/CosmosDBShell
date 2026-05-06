// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure;
using global::Azure.Core;
using global::Azure.Core.Pipeline;
using global::Azure.Identity;
using global::Azure.ResourceManager;
using global::Azure.ResourceManager.CosmosDB;
using global::Azure.ResourceManager.CosmosDB.Models;

internal static class CosmosArmResourceProvider
{
    public static async Task<ArmCosmosContext?> TryCreateContextAsync(
        TokenCredential? credential,
        Uri dataPlaneEndpoint,
        string? subscriptionId,
        string? resourceGroupName,
        string? accountName,
        CancellationToken token)
    {
        if (credential == null)
        {
            return null;
        }

        var armClient = new ArmClient(credential);

        var hasSubscription = !string.IsNullOrWhiteSpace(subscriptionId);
        var hasResourceGroup = !string.IsNullOrWhiteSpace(resourceGroupName);
        var hasAccount = !string.IsNullOrWhiteSpace(accountName);

        if (hasSubscription || hasResourceGroup || hasAccount)
        {
            if (!hasSubscription || !hasResourceGroup || !hasAccount)
            {
                throw new InvalidOperationException("Provide --subscription, --resource-group, and --account together to use explicit ARM account context.");
            }

            return await CreateExplicitContextAsync(armClient, dataPlaneEndpoint, subscriptionId!, resourceGroupName!, accountName!, token);
        }

        return await DiscoverContextAsync(armClient, dataPlaneEndpoint, token);
    }

    public static ArmCosmosContext RequireContext(ArmCosmosContext? context, string commandName)
    {
        if (context == null)
        {
            throw new CommandException(
                commandName,
                MessageService.GetString("error-arm-context-required"));
        }

        return context;
    }

    public static async Task<CosmosDBSqlDatabaseResource> GetDatabaseAsync(ArmCosmosContext context, string databaseName, CancellationToken token)
    {
        var response = await context.Account.GetCosmosDBSqlDatabases().GetIfExistsAsync(databaseName, token);
        var database = response.Value;
        if (database == null)
        {
            throw new RequestFailedException((int)HttpStatusCode.NotFound, $"Database '{databaseName}' was not found.");
        }

        return database;
    }

    public static async Task<CosmosDBSqlContainerResource> GetContainerAsync(ArmCosmosContext context, string databaseName, string containerName, CancellationToken token)
    {
        var database = await GetDatabaseAsync(context, databaseName, token);
        var response = await database.GetCosmosDBSqlContainers().GetIfExistsAsync(containerName, token);
        var container = response.Value;
        if (container == null)
        {
            throw new RequestFailedException((int)HttpStatusCode.NotFound, $"Container '{containerName}' was not found in database '{databaseName}'.");
        }

        return container;
    }

    public static async IAsyncEnumerable<CosmosDBSqlDatabaseResource> GetDatabasesAsync(ArmCosmosContext context)
    {
        await foreach (var database in context.Account.GetCosmosDBSqlDatabases().GetAllAsync())
        {
            yield return database;
        }
    }

    public static async IAsyncEnumerable<CosmosDBSqlContainerResource> GetContainersAsync(ArmCosmosContext context, string databaseName)
    {
        var database = await GetDatabaseAsync(context, databaseName, CancellationToken.None);
        await foreach (var container in database.GetCosmosDBSqlContainers().GetAllAsync())
        {
            yield return container;
        }
    }

    public static async Task<CosmosDBSqlDatabaseResource> CreateDatabaseAsync(ArmCosmosContext context, string databaseName, string? scale, int? maxRu, CancellationToken token)
    {
        var content = new CosmosDBSqlDatabaseCreateOrUpdateContent(
            context.Account.Data.Location,
            new CosmosDBSqlDatabaseResourceInfo(databaseName))
        {
            Options = CreateUpdateConfig(scale, maxRu),
        };

        var operation = await context.Account.GetCosmosDBSqlDatabases().CreateOrUpdateAsync(WaitUntil.Completed, databaseName, content, token);
        return operation.Value;
    }

    public static async Task<CosmosDBSqlContainerResource> CreateContainerAsync(
        ArmCosmosContext context,
        string databaseName,
        string containerName,
        IReadOnlyList<string> partitionKeyPaths,
        string? uniqueKey,
        string? indexPolicyJson,
        string? scale,
        int? maxRu,
        CancellationToken token)
    {
        var database = await GetDatabaseAsync(context, databaseName, token);
        var resource = new CosmosDBSqlContainerResourceInfo(containerName)
        {
            PartitionKey = new CosmosDBContainerPartitionKey
            {
                Kind = partitionKeyPaths.Count > 1 ? CosmosDBPartitionKind.MultiHash : CosmosDBPartitionKind.Hash,
                Version = partitionKeyPaths.Count > 1 ? 2 : 1,
            },
        };

        foreach (var path in partitionKeyPaths)
        {
            resource.PartitionKey.Paths.Add(path);
        }

        if (!string.IsNullOrWhiteSpace(uniqueKey))
        {
            var armUniqueKey = new CosmosDBUniqueKey();
            foreach (var path in uniqueKey.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                armUniqueKey.Paths.Add(path);
            }

            resource.UniqueKeys.Add(armUniqueKey);
        }

        if (!string.IsNullOrWhiteSpace(indexPolicyJson))
        {
            resource.IndexingPolicy = ReadArmModel<CosmosDBIndexingPolicy>(indexPolicyJson);
        }

        var content = new CosmosDBSqlContainerCreateOrUpdateContent(context.Account.Data.Location, resource)
        {
            Options = CreateUpdateConfig(scale, maxRu),
        };

        var operation = await database.GetCosmosDBSqlContainers().CreateOrUpdateAsync(WaitUntil.Completed, containerName, content, token);
        return operation.Value;
    }

    public static async Task DeleteDatabaseAsync(ArmCosmosContext context, string databaseName, CancellationToken token)
    {
        var database = await GetDatabaseAsync(context, databaseName, token);
        await database.DeleteAsync(WaitUntil.Completed, token);
    }

    public static async Task DeleteContainerAsync(ArmCosmosContext context, string databaseName, string containerName, CancellationToken token)
    {
        var container = await GetContainerAsync(context, databaseName, containerName, token);
        await container.DeleteAsync(WaitUntil.Completed, token);
    }

    public static IReadOnlyList<string> GetPartitionKeyPaths(CosmosDBSqlContainerResource container)
    {
        return container.Data.Resource.PartitionKey?.Paths?.ToArray() ?? [];
    }

    public static CosmosDBCreateUpdateConfig CreateUpdateConfig(string? scale, int? maxRu)
    {
        var ru = maxRu ?? 1000;
        if (string.Equals(scale, "manual", StringComparison.InvariantCultureIgnoreCase) ||
            string.Equals(scale, "m", StringComparison.InvariantCultureIgnoreCase))
        {
            return new CosmosDBCreateUpdateConfig
            {
                Throughput = ru,
            };
        }

        return new CosmosDBCreateUpdateConfig
        {
            AutoscaleMaxThroughput = ru,
        };
    }

    public static string WriteArmModel<T>(T model)
        where T : IPersistableModel<T>
    {
        return ModelReaderWriter.Write(model, ModelReaderWriterOptions.Json).ToString();
    }

    public static T ReadArmModel<T>(string json)
        where T : IPersistableModel<T>
    {
        var model = ModelReaderWriter.Read<T>(BinaryData.FromString(json), ModelReaderWriterOptions.Json);
        if (model == null)
        {
            throw new InvalidOperationException("Unable to read ARM model JSON.");
        }

        return model;
    }

    private static async Task<ArmCosmosContext> CreateExplicitContextAsync(
        ArmClient armClient,
        Uri dataPlaneEndpoint,
        string subscriptionId,
        string resourceGroupName,
        string accountName,
        CancellationToken token)
    {
        var accountResourceId = CosmosDBAccountResource.CreateResourceIdentifier(subscriptionId, resourceGroupName, accountName);
        var account = armClient.GetCosmosDBAccountResource(accountResourceId);
        var response = await account.GetAsync(token);
        var endpoint = new Uri(response.Value.Data.DocumentEndpoint);
        ValidateEndpoint(dataPlaneEndpoint, endpoint);

        return new ArmCosmosContext(armClient, accountResourceId, subscriptionId, resourceGroupName, accountName, endpoint, response.Value);
    }

    private static async Task<ArmCosmosContext?> DiscoverContextAsync(ArmClient armClient, Uri dataPlaneEndpoint, CancellationToken token)
    {
        var matches = new List<CosmosDBAccountResource>();

        await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync(token))
        {
            await foreach (var account in subscription.GetCosmosDBAccountsAsync(token))
            {
                if (EndpointEquals(dataPlaneEndpoint, new Uri(account.Data.DocumentEndpoint)))
                {
                    matches.Add(account);
                }
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException("Multiple Cosmos DB ARM accounts match the connected endpoint. Reconnect with --subscription, --resource-group, and --account.");
        }

        var match = matches[0];
        var id = match.Id;
        return new ArmCosmosContext(
            armClient,
            id,
            id.SubscriptionId ?? string.Empty,
            id.ResourceGroupName ?? string.Empty,
            match.Data.Name,
            new Uri(match.Data.DocumentEndpoint),
            match);
    }

    private static void ValidateEndpoint(Uri dataPlaneEndpoint, Uri armEndpoint)
    {
        if (!EndpointEquals(dataPlaneEndpoint, armEndpoint))
        {
            throw new InvalidOperationException($"The ARM account endpoint '{armEndpoint}' does not match the connected Cosmos DB endpoint '{dataPlaneEndpoint}'.");
        }
    }

    private static bool EndpointEquals(Uri left, Uri right)
    {
        return Uri.Compare(left, right, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}