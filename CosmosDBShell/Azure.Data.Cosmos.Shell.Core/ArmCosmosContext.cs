// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using global::Azure.Core;
using global::Azure.ResourceManager;
using global::Azure.ResourceManager.CosmosDB;

internal sealed class ArmCosmosContext(
    ArmClient armClient,
    ResourceIdentifier accountResourceId,
    string subscriptionId,
    string resourceGroupName,
    string accountName,
    Uri accountEndpoint,
    CosmosDBAccountResource accountResource)
{
    public ArmClient ArmClient { get; } = armClient;

    public ResourceIdentifier AccountResourceId { get; } = accountResourceId;

    public string SubscriptionId { get; } = subscriptionId;

    public string ResourceGroupName { get; } = resourceGroupName;

    public string AccountName { get; } = accountName;

    public Uri AccountEndpoint { get; } = accountEndpoint;

    public CosmosDBAccountResource Account { get; } = accountResource;
}