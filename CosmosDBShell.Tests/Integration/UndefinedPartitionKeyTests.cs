// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class UndefinedPartitionKeyTests : EmulatorTestBase
{
    [Fact]
    public async Task ItemCommands_AddressUndefinedScalarPartition()
    {
        var token = TestContext.Current.CancellationToken;
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        using var client = new CosmosClient(connectionString, CreateClientOptions());
        var database = (await client.CreateDatabaseAsync($"UndefinedKeys_{Guid.NewGuid():N}", cancellationToken: token)).Database;
        var container = (await database.CreateContainerAsync(
            new ContainerProperties("Items", "/pk"),
            cancellationToken: token)).Container;

        try
        {
            Shell.State = new ConnectedState(new CosmosClient(connectionString, CreateClientOptions()));

            var create = await RunScriptAsync(
                $"mkitem '{{\"id\":\"shared\",\"value\":1}}' --database={database.Id} --container={container.Id}");
            Assert.False(create.IsError, FormatError(create));
            Assert.Equal(1, GetJson(create).GetProperty("created").GetInt32());

            var upsert = await RunScriptAsync(
                $"mkitem --force '{{\"id\":\"shared\",\"value\":1}}' --database={database.Id} --container={container.Id}");
            Assert.False(upsert.IsError, FormatError(upsert));
            Assert.Equal(1, GetJson(upsert).GetProperty("replaced").GetInt32());

            var read = await RunScriptAsync(
                $"print shared '{{}}' --database={database.Id} --container={container.Id}");
            Assert.False(read.IsError, FormatError(read));
            Assert.Equal(1, GetJson(read).GetProperty("value").GetInt32());

            var patch = await RunScriptAsync(
                $"patch set shared '{{}}' /value 2 --database={database.Id} --container={container.Id}");
            Assert.False(patch.IsError, FormatError(patch));

            var replace = await RunScriptAsync(
                $"replace '{{\"id\":\"shared\",\"value\":3}}' --database={database.Id} --container={container.Id}");
            Assert.False(replace.IsError, FormatError(replace));

            var batchJson = """[{"op":"patch","id":"shared","operations":[{"op":"set","path":"/value","value":4}]}]""";
            var batch = await RunScriptAsync(
                $"batch run '{batchJson}' --partition-key '{{}}' --database={database.Id} --container={container.Id}");
            Assert.False(batch.IsError, FormatError(batch));

            var watchOutput = CaptureOutputFile();
            var watch = await RunScriptAsync(
                $"watch --from-beginning --partition-key '{{}}' --max=1 --interval=.1 --database={database.Id} --container={container.Id}");
            Assert.False(watch.IsError, FormatError(watch));
            Assert.Contains("\"id\": \"shared\"", await ReadRedirectAsync(watchOutput));
            Shell.StdOutRedirect = null;

            var finalRead = await RunScriptAsync(
                $"print shared '{{}}' --database={database.Id} --container={container.Id}");
            Assert.False(finalRead.IsError, FormatError(finalRead));
            Assert.Equal("shared", GetJson(finalRead).GetProperty("id").GetString());

            var remove = await RunScriptAsync(
                $"rm shared --key=id --partition-key '{{}}' --database={database.Id} --container={container.Id}");
            Assert.False(remove.IsError, FormatError(remove));
            Assert.Equal(1, GetJson(remove).GetProperty("count").GetInt32());

            var bulkJson = """[{"id":"bulk-none"},{"id":"bulk-string","pk":"tenant"}]""";
            var bulkCreate = await RunScriptAsync(
                $"mkitem '{bulkJson}' --database={database.Id} --container={container.Id}");
            Assert.False(bulkCreate.IsError, FormatError(bulkCreate));
            Assert.Equal(2, GetJson(bulkCreate).GetProperty("created").GetInt32());
            Assert.Equal(0, GetJson(bulkCreate).GetProperty("failed").GetInt32());

            var bulkUpsert = await RunScriptAsync(
                $"mkitem --force '{bulkJson}' --database={database.Id} --container={container.Id}");
            Assert.False(bulkUpsert.IsError, FormatError(bulkUpsert));
            Assert.Equal(2, GetJson(bulkUpsert).GetProperty("replaced").GetInt32());
            Assert.Equal(0, GetJson(bulkUpsert).GetProperty("failed").GetInt32());

            var removeAll = await RunScriptAsync(
                $"rm * --database={database.Id} --container={container.Id}");
            Assert.False(removeAll.IsError, FormatError(removeAll));
            Assert.Equal(2, GetJson(removeAll).GetProperty("count").GetInt32());
        }
        finally
        {
            Shell.StdOutRedirect = null;
            await database.DeleteAsync(cancellationToken: CancellationToken.None);
        }
    }

    [Fact]
    public async Task HierarchicalMutations_TargetUndefinedComponentsWithoutTouchingCollisions()
    {
        var token = TestContext.Current.CancellationToken;
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        using var client = new CosmosClient(connectionString, CreateClientOptions());
        var database = (await client.CreateDatabaseAsync($"UndefinedHpk_{Guid.NewGuid():N}", cancellationToken: token)).Database;
        var container = (await database.CreateContainerAsync(
            new ContainerProperties("Items", new[] { "/tenant", "/region" }),
            cancellationToken: token)).Container;

        var missingFirstKey = new PartitionKeyBuilder().AddNoneType().Add("west").Build();
        var stringFirstKey = new PartitionKeyBuilder().Add("undefined").Add("west").Build();
        var missingLaterKey = new PartitionKeyBuilder().Add("tenant-a").AddNoneType().Build();
        var nullLaterKey = new PartitionKeyBuilder().Add("tenant-a").AddNullValue().Build();
        var stringLaterKey = new PartitionKeyBuilder().Add("tenant-a").Add("undefined").Build();

        try
        {
            await container.CreateItemAsync(
                new { id = "same-first", region = "west", marker = "undefined-first" },
                missingFirstKey,
                cancellationToken: token);
            await container.CreateItemAsync(
                new { id = "same-first", tenant = "undefined", region = "west", marker = "string-first" },
                stringFirstKey,
                cancellationToken: token);
            await container.CreateItemAsync(
                new { id = "same-later", tenant = "tenant-a", marker = "delete-target" },
                missingLaterKey,
                cancellationToken: token);
            await container.CreateItemAsync(
                new { id = "null-later", tenant = "tenant-a", region = (string?)null, marker = "null-later" },
                nullLaterKey,
                cancellationToken: token);
            await container.CreateItemAsync(
                new { id = "string-later", tenant = "tenant-a", region = "undefined", marker = "string-later" },
                stringLaterKey,
                cancellationToken: token);

            Shell.State = new ConnectedState(new CosmosClient(connectionString, CreateClientOptions()));

            var update = await RunScriptAsync(
                $"mkitem --force '{{\"id\":\"same-first\",\"region\":\"west\",\"marker\":\"updated-first\"}}' --database={database.Id} --container={container.Id}");
            Assert.False(update.IsError, FormatError(update));
            Assert.Equal(
                "updated-first",
                await ReadMarkerAsync(container, "same-first", missingFirstKey, token));
            Assert.Equal(
                "string-first",
                await ReadMarkerAsync(container, "same-first", stringFirstKey, token));

            var remove = await RunScriptAsync(
                $"rm delete-target --key=marker --database={database.Id} --container={container.Id}");
            Assert.False(remove.IsError, FormatError(remove));
            Assert.Equal(1, GetJson(remove).GetProperty("count").GetInt32());

            using (var deleted = await container.ReadItemStreamAsync("same-later", missingLaterKey, cancellationToken: token))
            {
                Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
            }

            Assert.Equal(
                "null-later",
                await ReadMarkerAsync(container, "null-later", nullLaterKey, token));
            Assert.Equal(
                "string-later",
                await ReadMarkerAsync(container, "string-later", stringLaterKey, token));
        }
        finally
        {
            await database.DeleteAsync(cancellationToken: CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkItemCommands_ContinueAfterInvalidPartitionKey()
    {
        var token = TestContext.Current.CancellationToken;
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        using var client = new CosmosClient(connectionString, CreateClientOptions());
        var database = (await client.CreateDatabaseAsync($"InvalidKeys_{Guid.NewGuid():N}", cancellationToken: token)).Database;
        var container = (await database.CreateContainerAsync(
            new ContainerProperties("Items", "/pk"),
            cancellationToken: token)).Container;

        try
        {
            Shell.State = new ConnectedState(new CosmosClient(connectionString, CreateClientOptions()));

            var createPayload = """[{"id":"mk-invalid","pk":[]},{"id":"mk-valid","pk":"tenant"}]""";
            var create = await RunScriptAsync(
                $"mkitem '{createPayload}' --database={database.Id} --container={container.Id}");
            Assert.True(create.IsError);
            Assert.Contains("Failed to write 1 of 2 items.", FormatError(create));

            var createdItem = await RunScriptAsync(
                $"print mk-valid tenant --database={database.Id} --container={container.Id}");
            Assert.False(createdItem.IsError, FormatError(createdItem));
            Assert.Equal("mk-valid", GetJson(createdItem).GetProperty("id").GetString());

            var seed = await RunScriptAsync(
                $"mkitem '{{\"id\":\"replace-valid\",\"pk\":\"tenant\",\"value\":1}}' --database={database.Id} --container={container.Id}");
            Assert.False(seed.IsError, FormatError(seed));

            var replacePayload = """[{"id":"replace-invalid","pk":[]},{"id":"replace-valid","pk":"tenant","value":2}]""";
            var replace = await RunScriptAsync(
                $"replace '{replacePayload}' --database={database.Id} --container={container.Id}");
            Assert.True(replace.IsError);
            Assert.Contains("Failed to replace 1 of 2 items.", FormatError(replace));

            var replacedItem = await RunScriptAsync(
                $"print replace-valid tenant --database={database.Id} --container={container.Id}");
            Assert.False(replacedItem.IsError, FormatError(replacedItem));
            Assert.Equal(2, GetJson(replacedItem).GetProperty("value").GetInt32());
        }
        finally
        {
            await database.DeleteAsync(cancellationToken: CancellationToken.None);
        }
    }

    [Fact]
    public async Task PipedRm_ExcludedItemWithInvalidPartitionKeyDoesNotBlockDeletion()
    {
        var token = TestContext.Current.CancellationToken;
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        using var client = new CosmosClient(connectionString, CreateClientOptions());
        var database = (await client.CreateDatabaseAsync($"PipedRm_{Guid.NewGuid():N}", cancellationToken: token)).Database;
        var container = (await database.CreateContainerAsync(
            new ContainerProperties("Items", "/pk"),
            cancellationToken: token)).Container;

        try
        {
            await container.CreateItemAsync(new { id = "target", pk = "tenant" }, new PartitionKey("tenant"), cancellationToken: token);
            Shell.State = new ConnectedState(new CosmosClient(connectionString, CreateClientOptions()));

            // filter parses the literal into JSON so rm receives structured piped items.
            var remove = await RunScriptAsync(
                """echo '[{"id":"skip","pk":[]},{"id":"target","pk":"tenant"}]' | filter '.' | """
                    + $"rm target --key=id --database={database.Id} --container={container.Id}");

            Assert.False(remove.IsError, FormatError(remove));
            using var read = await container.ReadItemStreamAsync("target", new PartitionKey("tenant"), cancellationToken: token);
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        }
        finally
        {
            await database.DeleteAsync(cancellationToken: CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PipedRm_ProjectionWithoutPartitionKeyDoesNotInferUndefinedPartition(bool arrayInput)
    {
        var token = TestContext.Current.CancellationToken;
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        using var client = new CosmosClient(connectionString, CreateClientOptions());
        var database = (await client.CreateDatabaseAsync($"PipedProjection_{Guid.NewGuid():N}", cancellationToken: token)).Database;
        var container = (await database.CreateContainerAsync(
            new ContainerProperties("Items", "/pk"),
            cancellationToken: token)).Container;

        try
        {
            await container.CreateItemAsync(new { id = "shared", pk = "tenant" }, new PartitionKey("tenant"), cancellationToken: token);
            await container.CreateItemAsync(new { id = "shared" }, PartitionKey.None, cancellationToken: token);
            Shell.State = new ConnectedState(new CosmosClient(connectionString, CreateClientOptions()));
            var projection = arrayInput ? """[{"id":"shared"}]""" : """{"id":"shared"}""";
            var target = $"rm shared --key=id --database={database.Id} --container={container.Id}";

            var inferred = await RunScriptAsync($"echo '{projection}' | filter '.' | {target}");

            Assert.False(inferred.IsError, FormatError(inferred));
            using (var tenantRead = await container.ReadItemStreamAsync("shared", new PartitionKey("tenant"), cancellationToken: token))
            using (var noneRead = await container.ReadItemStreamAsync("shared", PartitionKey.None, cancellationToken: token))
            {
                Assert.Equal(HttpStatusCode.OK, tenantRead.StatusCode);
                Assert.Equal(HttpStatusCode.OK, noneRead.StatusCode);
            }

            var explicitNone = await RunScriptAsync($"echo '{projection}' | filter '.' | {target} --partition-key '{{}}'");

            Assert.False(explicitNone.IsError, FormatError(explicitNone));
            using (var tenantRead = await container.ReadItemStreamAsync("shared", new PartitionKey("tenant"), cancellationToken: token))
            using (var noneRead = await container.ReadItemStreamAsync("shared", PartitionKey.None, cancellationToken: token))
            {
                Assert.Equal(HttpStatusCode.OK, tenantRead.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, noneRead.StatusCode);
            }
        }
        finally
        {
            await database.DeleteAsync(cancellationToken: CancellationToken.None);
        }
    }

    private static CosmosClientOptions CreateClientOptions() => new()
    {
        ConnectionMode = ConnectionMode.Gateway,
        ServerCertificateCustomValidationCallback = (_, _, _) => true,
    };

    private static async Task<string?> ReadMarkerAsync(
        Container container,
        string id,
        PartitionKey partitionKey,
        CancellationToken token)
    {
        using var response = await container.ReadItemStreamAsync(id, partitionKey, cancellationToken: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(response.Content, cancellationToken: token);
        return document.RootElement.GetProperty("marker").GetString();
    }
}
