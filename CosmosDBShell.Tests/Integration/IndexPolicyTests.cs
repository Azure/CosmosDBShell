// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

public class IndexPolicyTests : ConnectedEmulatorTestBase
{
    [Fact]
    public async Task IndexAdd_SelectivePolicy_PreservesServiceReturnedPolicyAfterReadback()
    {
        var databaseName = $"IndexAdd_{Guid.NewGuid():N}";
        CreatedDatabases.Add(databaseName);
        var database = await CosmosClient.CreateDatabaseAsync(databaseName, cancellationToken: TestContext.Current.CancellationToken);
        await database.Database.CreateContainerAsync("Addresses", "/addressId", cancellationToken: TestContext.Current.CancellationToken);

        const string policy = """
            {"indexingMode":"consistent","automatic":true,
             "includedPaths":[{"path":"/addressId/?"},{"path":"/docType/?"},{"path":"/rowguid/?"}],
             "excludedPaths":[{"path":"/\"_etag\"/?"},{"path":"/*"}]}
            """;
        var target = $"--database={databaseName} --container=Addresses";
        var set = await ExecuteAsync($"index set '{policy}' {target}");
        Assert.False(set.IsError, FormatError(set));

        var container = CosmosClient.GetContainer(databaseName, "Addresses");
        var before = await container.ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        var expected = JObject.Parse(DataPlaneCosmosResourceOperations.SerializeIndexingPolicy(before.Resource.IndexingPolicy));

        before.Resource.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = "/city/?" });
        await container.ReplaceContainerAsync(before.Resource, cancellationToken: TestContext.Current.CancellationToken);
        var sdkControl = await container.ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(sdkControl.Resource.IndexingPolicy.IncludedPaths, path => path.Path == "/city/?");
        var reset = await ExecuteAsync($"index set '{policy}' {target}");
        Assert.False(reset.IsError, FormatError(reset));

        var add = await ExecuteAsync($"index add /city/? {target}");
        Assert.False(add.IsError, $"{FormatError(add)}\nOriginal policy: {expected}");
        var repeated = await ExecuteAsync($"index add /city/? {target}");
        Assert.False(repeated.IsError, FormatError(repeated));

        var after = await container.ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(after.Resource.IndexingPolicy.IncludedPaths, path => path.Path == "/city/?");
        after.Resource.IndexingPolicy.IncludedPaths.Remove(
            after.Resource.IndexingPolicy.IncludedPaths.Single(path => path.Path == "/city/?"));
        var actual = JObject.Parse(DataPlaneCosmosResourceOperations.SerializeIndexingPolicy(after.Resource.IndexingPolicy));
        Assert.True(JToken.DeepEquals(expected, actual), $"Expected: {expected}\nActual: {actual}");

        var remove = await ExecuteAsync($"index remove /city/? {target}");
        Assert.False(remove.IsError, FormatError(remove));
        var removed = await container.ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        var restored = JObject.Parse(DataPlaneCosmosResourceOperations.SerializeIndexingPolicy(removed.Resource.IndexingPolicy));
        Assert.True(JToken.DeepEquals(expected, restored), $"Expected: {expected}\nActual: {restored}");
    }
}
