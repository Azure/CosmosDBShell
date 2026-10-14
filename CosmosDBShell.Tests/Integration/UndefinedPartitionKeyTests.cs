// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;

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

            await container.Scripts.CreateStoredProcedureAsync(
                new StoredProcedureProperties("noneProbe", "function () { return 'ok'; }"),
                cancellationToken: token);
            var sproc = await RunScriptAsync(
                $"sproc exec noneProbe [] --partition-key '{{}}' --database={database.Id} --container={container.Id}");
            Assert.False(sproc.IsError, FormatError(sproc));

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
        }
        finally
        {
            Shell.StdOutRedirect = null;
            await database.DeleteAsync(cancellationToken: CancellationToken.None);
        }
    }

    private static CosmosClientOptions CreateClientOptions() => new()
    {
        ConnectionMode = ConnectionMode.Gateway,
        ServerCertificateCustomValidationCallback = (_, _, _) => true,
    };
}
