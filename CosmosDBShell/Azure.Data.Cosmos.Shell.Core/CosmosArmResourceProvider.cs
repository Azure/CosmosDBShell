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
                throw new ShellException(MessageService.GetString("error-arm-context-incomplete"));
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
        if (!response.HasValue || response.Value is null)
        {
            throw new RequestFailedException((int)HttpStatusCode.NotFound, $"Database '{databaseName}' was not found.");
        }

        return response.Value;
    }

    public static async Task<CosmosDBSqlContainerResource> GetContainerAsync(ArmCosmosContext context, string databaseName, string containerName, CancellationToken token)
    {
        var database = await GetDatabaseAsync(context, databaseName, token);
        var response = await database.GetCosmosDBSqlContainers().GetIfExistsAsync(containerName, token);
        if (!response.HasValue || response.Value is null)
        {
            throw new RequestFailedException((int)HttpStatusCode.NotFound, $"Container '{containerName}' was not found in database '{databaseName}'.");
        }

        return response.Value;
    }

    public static async IAsyncEnumerable<CosmosDBSqlDatabaseResource> GetDatabasesAsync(ArmCosmosContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
    {
        await foreach (var database in context.Account.GetCosmosDBSqlDatabases().GetAllAsync(token))
        {
            yield return database;
        }
    }

    public static async IAsyncEnumerable<CosmosDBSqlContainerResource> GetContainersAsync(ArmCosmosContext context, string databaseName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
    {
        var database = await GetDatabaseAsync(context, databaseName, token);
        await foreach (var container in database.GetCosmosDBSqlContainers().GetAllAsync(cancellationToken: token))
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
        if (model is null)
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
        CosmosDBAccountResource? singleMatch = null;
        bool multipleMatches = false;

        await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync(token))
        {
            await foreach (var account in subscription.GetCosmosDBAccountsAsync(token))
            {
                if (!EndpointEquals(dataPlaneEndpoint, new Uri(account.Data.DocumentEndpoint)))
                {
                    continue;
                }

                if (singleMatch is null)
                {
                    singleMatch = account;
                    continue;
                }

                // We already have one match; finding a second is enough to know the discovery
                // is ambiguous, so stop scanning further subscriptions/accounts.
                multipleMatches = true;
                break;
            }

            if (multipleMatches)
            {
                break;
            }
        }

        if (multipleMatches)
        {
            throw new ShellException(MessageService.GetString("error-arm-context-ambiguous"));
        }

        if (singleMatch is null)
        {
            return null;
        }

        var id = singleMatch.Id;
        return new ArmCosmosContext(
            armClient,
            id,
            id.SubscriptionId ?? string.Empty,
            id.ResourceGroupName ?? string.Empty,
            singleMatch.Data.Name,
            new Uri(singleMatch.Data.DocumentEndpoint),
            singleMatch);
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