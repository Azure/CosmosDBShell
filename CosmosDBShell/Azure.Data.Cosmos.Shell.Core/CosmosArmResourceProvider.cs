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
    // Known limitation: when the connection has no token credential (account key,
    // COSMOSDB_SHELL_TOKEN, emulator), no ARM context is attached and the resource
    // facade falls back to the data plane. For a real Azure Cosmos DB account that
    // is configured with strict native data-plane RBAC, that fallback path will fail
    // for control-plane operations (mkdb/mkcon/rmdb/rmcon/indexpolicy) regardless of
    // the static credential supplied. Resolving this would require either prompting
    // the user to associate the account with an Azure subscription or extending the
    // connection-string flow to capture (subscription, resource group, account) so
    // an ARM context can be built without a token credential. See vscode-cosmosdb
    // PR #3016 for the analogous gap in the VS Code extension.
    public static async Task<ArmCosmosContext?> TryCreateContextAsync(
        TokenCredential? credential,
        Uri dataPlaneEndpoint,
        string? subscriptionId,
        string? resourceGroupName,
        string? accountName,
        Uri? authorityHost,
        CancellationToken token)
    {
        if (credential == null)
        {
            return null;
        }

        var armOptions = new ArmClientOptions
        {
            Environment = ResolveArmEnvironment(authorityHost, dataPlaneEndpoint),
        };
        var armClient = new ArmClient(credential, defaultSubscriptionId: null, options: armOptions);

        var hasSubscription = !string.IsNullOrWhiteSpace(subscriptionId);
        var hasResourceGroup = !string.IsNullOrWhiteSpace(resourceGroupName);
        var hasAccount = !string.IsNullOrWhiteSpace(accountName);

        if (hasSubscription || hasResourceGroup || hasAccount)
        {
            if (!hasSubscription || !hasResourceGroup)
            {
                throw new ShellException(MessageService.GetString("error-arm-context-incomplete"));
            }

            var resolvedAccountName = hasAccount ? accountName! : GetAccountNameFromEndpoint(dataPlaneEndpoint);
            return await CreateExplicitContextAsync(armClient, dataPlaneEndpoint, subscriptionId!, resourceGroupName!, resolvedAccountName, token);
        }

        return await DiscoverContextAsync(armClient, dataPlaneEndpoint, token);
    }

    /// <summary>
    /// Maps a credential authority host (or, when absent, the data-plane Cosmos
    /// endpoint suffix) to the matching <see cref="ArmEnvironment"/>. Falls back
    /// to <see cref="ArmEnvironment.AzurePublicCloud"/> when the host does not
    /// look like a known sovereign cloud, which preserves the prior default.
    /// </summary>
    internal static ArmEnvironment ResolveArmEnvironment(Uri? authorityHost, Uri dataPlaneEndpoint)
    {
        var host = authorityHost?.Host;
        if (!string.IsNullOrEmpty(host))
        {
            if (host.EndsWith("login.microsoftonline.us", StringComparison.OrdinalIgnoreCase))
            {
                return ArmEnvironment.AzureGovernment;
            }

            if (host.EndsWith("login.chinacloudapi.cn", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("login.partner.microsoftonline.cn", StringComparison.OrdinalIgnoreCase))
            {
                return ArmEnvironment.AzureChina;
            }

            if (host.EndsWith("login.microsoftonline.de", StringComparison.OrdinalIgnoreCase))
            {
                return ArmEnvironment.AzureGermany;
            }
        }

        // Fall back to inspecting the Cosmos endpoint suffix when the user did not
        // pass --authority-host (sovereign-cloud users typically still use the
        // default authority on a sovereign endpoint).
        var endpointHost = dataPlaneEndpoint.Host;
        if (endpointHost.EndsWith(".documents.azure.us", StringComparison.OrdinalIgnoreCase))
        {
            return ArmEnvironment.AzureGovernment;
        }

        if (endpointHost.EndsWith(".documents.azure.cn", StringComparison.OrdinalIgnoreCase))
        {
            return ArmEnvironment.AzureChina;
        }

        if (endpointHost.EndsWith(".documents.microsoftazure.de", StringComparison.OrdinalIgnoreCase))
        {
            return ArmEnvironment.AzureGermany;
        }

        return ArmEnvironment.AzurePublicCloud;
    }

    internal static string GetAccountNameFromEndpoint(Uri dataPlaneEndpoint)
    {
        var host = dataPlaneEndpoint.Host;
        var firstDot = host.IndexOf('.', StringComparison.Ordinal);
        return firstDot > 0 ? host[..firstDot] : host;
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
        CosmosDBIndexingPolicy? indexingPolicy,
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

        if (indexingPolicy is not null)
        {
            resource.IndexingPolicy = indexingPolicy;
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