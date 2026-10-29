// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ResourceCreationTests : ConnectedEmulatorTestBase
{
    [Theory]
    [InlineData("mkdb")]
    [InlineData("create database")]
    public async Task DatabaseCreation_ReportsCreatedThenExisting(string command)
    {
        var name = $"Creation_{Guid.NewGuid():N}";
        CreatedDatabases.Add(name);
        await AssertCreationAsync($"{command} {name}", "database", name, true);
        var before = await CosmosClient.GetDatabase(name).ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        await AssertCreationAsync($"{command} {name}", "database", name, false);
        var after = await CosmosClient.GetDatabase(name).ReadAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(JsonConvert.SerializeObject(before.Resource), JsonConvert.SerializeObject(after.Resource));
    }

    [Theory]
    [InlineData("mkcon", false)]
    [InlineData("create container", false)]
    [InlineData("mkcon", true)]
    [InlineData("create container", true)]
    public async Task ContainerCreation_PreservesExistingResourceAndData(string command, bool sdkCreated)
    {
        var name = $"Creation_{Guid.NewGuid():N}";
        CreatedDatabases.Add(name);
        var database = await CosmosClient.CreateDatabaseAsync(name, cancellationToken: TestContext.Current.CancellationToken);
        if (sdkCreated)
        {
            var properties = new ContainerProperties("Items", "/pk")
            {
                PartitionKeyDefinitionVersion = PartitionKeyDefinitionVersion.V2,
                DefaultTimeToLive = 3600,
            };
            properties.UniqueKeyPolicy.UniqueKeys.Add(new UniqueKey { Paths = { "/email" } });
            await database.Database.CreateContainerAsync(properties, cancellationToken: TestContext.Current.CancellationToken);
        }

        var text = $"{command} Items /pk --database={name}";
        await AssertCreationAsync(text, "container", "Items", !sdkCreated);
        var container = CosmosClient.GetContainer(name, "Items");
        var before = await container.ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        var item = new { id = "retained", pk = "test", email = "test@example.test" };
        await container.CreateItemAsync(item, new PartitionKey("test"), cancellationToken: TestContext.Current.CancellationToken);
        await AssertCreationAsync(text, "container", "Items", false);
        var after = await container.ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(JToken.DeepEquals(JObject.Parse(JsonConvert.SerializeObject(before.Resource)), JObject.Parse(JsonConvert.SerializeObject(after.Resource))));
        if (sdkCreated)
        {
            Assert.Equal(PartitionKeyDefinitionVersion.V2, after.Resource.PartitionKeyDefinitionVersion);
        }

        var retained = await container.ReadItemAsync<JObject>("retained", new PartitionKey("test"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("test@example.test", (string?)retained.Resource["email"]);

        var mismatch = await Assert.ThrowsAsync<ArgumentException>(() =>
            Shell.RunCommandAsync(new CommandState(), $"{command} Items /different --database={name}", TestContext.Current.CancellationToken));
        Assert.Equal("PartitionKey", mismatch.ParamName);
        var unchanged = await container.ReadContainerAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(JsonConvert.SerializeObject(after.Resource), JsonConvert.SerializeObject(unchanged.Resource));
    }

    private async Task AssertCreationAsync(string command, string type, string id, bool created)
    {
        var state = await Shell.RunCommandAsync(new CommandState(), command, TestContext.Current.CancellationToken);
        Assert.False(state.IsError, FormatError(state));
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal(type, result.GetProperty("type").GetString());
        Assert.Equal(id, result.GetProperty("id").GetString());
        Assert.Equal(created, result.GetProperty("created").GetBoolean());
        Assert.Equal(created ? "created" : "existing", result.GetProperty("outcome").GetString());
    }
}
