// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class BulkOperationTests : ConnectedEmulatorTestBase
{
    private readonly List<string> files = [];
    private Database database = null!;
    private Container container = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var databaseName = $"BulkTests_{Guid.NewGuid():N}";
        CreatedDatabases.Add(databaseName);
        this.database = (await CosmosClient.CreateDatabaseAsync(databaseName, cancellationToken: TestContext.Current.CancellationToken)).Database;
        this.container = (await this.database.CreateContainerAsync("items", "/pk", cancellationToken: TestContext.Current.CancellationToken)).Container;
        Shell.IsInteractiveSession = () => false;
        await this.NavigateAsync(Shell);
    }

    public override void Dispose()
    {
        try
        {
            foreach (var path in this.files)
            {
                File.Delete(path);
            }
        }
        finally
        {
            base.Dispose();
        }
    }

    [Fact]
    public async Task Run_MixedOperationsAcrossPartitions_ExceedsBatchLimitAndPreservesItemOrder()
    {
        var token = TestContext.Current.CancellationToken;
        await this.container.CreateItemAsync(new { id = "counter", pk = "a", count = 0 }, cancellationToken: token);
        await this.container.CreateItemAsync(new { id = "replace", pk = "b", value = "old" }, cancellationToken: token);
        await this.container.CreateItemAsync(new { id = "delete", pk = "c" }, cancellationToken: token);
        var operations = Enumerable.Range(0, 105)
            .Select(index => (object)new { op = "create", item = new { id = $"item-{index}", pk = $"partition-{index % 3}", value = index } })
            .ToList();
        operations.Add(new { op = "upsert", item = new { id = "upsert", pk = "d", value = "new" } });
        operations.Add(new { op = "replace", item = new { id = "replace", pk = "b", value = "new" } });
        operations.Add(new { op = "patch", id = "counter", partitionKey = "a", operations = new[] { new { op = "set", path = "/count", value = 10 } } });
        operations.Add(new { op = "patch", id = "counter", partitionKey = "a", operations = new[] { new { op = "incr", path = "/count", value = 1 } } });
        operations.Add(new { op = "delete", id = "delete", partitionKey = "c" });
        var plan = await this.WriteOperationsAsync(operations.ToArray());

        var (_, preview) = await this.RunJsonAsync($"bulk run '{plan}' --dry-run");
        Assert.True(preview.GetProperty("dryRun").GetBoolean());
        Assert.Equal(110, preview.GetProperty("operationCount").GetInt64());
        Assert.Equal(0, preview.GetProperty("attempted").GetInt64());
        Assert.Equal(0, (await this.ReadItemAsync("counter", new PartitionKey("a"))).GetProperty("count").GetInt32());
        await this.AssertMissingAsync("item-0", new PartitionKey("partition-0"));
        await this.AssertMissingAsync("upsert", new PartitionKey("d"));
        Assert.Equal("old", (await this.ReadItemAsync("replace", new PartitionKey("b"))).GetProperty("value").GetString());
        await this.ReadItemAsync("delete", new PartitionKey("c"));

        var (state, result) = await this.RunJsonAsync($"bulk run '{plan}' --concurrency 8 --yes");
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(110, result.GetProperty("attempted").GetInt64());
        Assert.Equal(110, result.GetProperty("succeeded").GetInt64());
        Assert.Equal(0, result.GetProperty("failed").GetInt64());
        Assert.True(state.RequestCharge is > 0);
        Assert.Equal(state.RequestCharge, result.GetProperty("requestCharge").GetDouble());
        for (var index = 0; index < 105; index++)
        {
            var item = await this.ReadItemAsync($"item-{index}", new PartitionKey($"partition-{index % 3}"));
            Assert.Equal(index, item.GetProperty("value").GetInt32());
        }

        Assert.Equal(11, (await this.ReadItemAsync("counter", new PartitionKey("a"))).GetProperty("count").GetInt32());
        Assert.Equal("new", (await this.ReadItemAsync("replace", new PartitionKey("b"))).GetProperty("value").GetString());
        Assert.Equal("new", (await this.ReadItemAsync("upsert", new PartitionKey("d"))).GetProperty("value").GetString());
        await this.AssertMissingAsync("delete", new PartitionKey("c"));
    }

    [Fact]
    public async Task SavedSelection_PreservesHierarchicalKeysAndETags_WithoutReselectingOnRun()
    {
        var token = TestContext.Current.CancellationToken;
        this.container = (await this.database.CreateContainerAsync(
            new ContainerProperties("hierarchical", new[] { "/tenant/id", "/region", "/active" }),
            cancellationToken: token)).Container;
        await this.NavigateAsync(Shell);
        var firstKey = new PartitionKeyBuilder().Add("tenant-a").Add(7).Add(true).Build();
        var secondKey = new PartitionKeyBuilder().Add("tenant-a").Add(8).Add(false).Build();
        await this.container.CreateItemAsync(new { id = "first", tenant = new { id = "tenant-a" }, region = 7, active = true, version = 1, count = 0 }, firstKey, cancellationToken: token);
        await this.container.CreateItemAsync(new { id = "second", tenant = new { id = "tenant-a" }, region = 8, active = false, version = 1, count = 0 }, secondKey, cancellationToken: token);
        var firstBefore = await this.ReadItemAsync("first", firstKey);
        var secondBefore = await this.ReadItemAsync("second", secondKey);
        var plan = this.PathFor("selection.jsonl");
        var journal = this.PathFor("selection.journal");

        var (_, preview) = await this.RunJsonAsync(
            $$"""bulk patch --where "c.version = 1" --operations '[{"op":"incr","path":"/count","value":1}]' --etag --dry-run --save '{{plan}}' --yes""");
        Assert.Equal(2, preview.GetProperty("operationCount").GetInt64());
        Assert.Equal(0, preview.GetProperty("attempted").GetInt64());
        var saved = await ReadJsonLinesAsync(plan);
        Assert.Equal(2, saved.Length);
        var firstOperation = Assert.Single(saved, operation => operation.GetProperty("id").GetString() == "first");
        Assert.Equal("""["tenant-a",7,true]""", firstOperation.GetProperty("partitionKey").GetRawText());
        Assert.Equal(firstBefore.GetProperty("_etag").GetString(), firstOperation.GetProperty("ifMatch").GetString());
        var secondOperation = Assert.Single(saved, operation => operation.GetProperty("id").GetString() == "second");
        Assert.Equal("""["tenant-a",8,false]""", secondOperation.GetProperty("partitionKey").GetRawText());
        Assert.Equal(secondBefore.GetProperty("_etag").GetString(), secondOperation.GetProperty("ifMatch").GetString());
        Assert.Equal(firstBefore.GetProperty("_etag").GetString(), (await this.ReadItemAsync("first", firstKey)).GetProperty("_etag").GetString());
        Assert.Equal(secondBefore.GetProperty("_etag").GetString(), (await this.ReadItemAsync("second", secondKey)).GetProperty("_etag").GetString());
        await this.container.PatchItemAsync<object>("second", secondKey, [PatchOperation.Set("/count", 99)], cancellationToken: token);
        await this.container.CreateItemAsync(new { id = "later", tenant = new { id = "tenant-a" }, region = 7, active = true, version = 1, count = 0 }, firstKey, cancellationToken: token);

        var (_, applied) = await this.RunJsonAsync($"bulk run '{plan}' --journal '{journal}' --continue-on-error --yes", expectError: true);
        Assert.Equal(1, applied.GetProperty("succeeded").GetInt64());
        Assert.Equal(1, applied.GetProperty("failed").GetInt64());
        Assert.Equal(412, Assert.Single(applied.GetProperty("errors").EnumerateArray()).GetProperty("statusCode").GetInt32());
        Assert.Equal(1, (await this.ReadItemAsync("first", firstKey)).GetProperty("count").GetInt32());
        Assert.Equal(99, (await this.ReadItemAsync("second", secondKey)).GetProperty("count").GetInt32());
        Assert.Equal(0, (await this.ReadItemAsync("later", firstKey)).GetProperty("count").GetInt32());

        var (_, deleted) = await this.RunJsonAsync("""bulk delete --where "c.id = 'first'" --etag --yes""");
        Assert.Equal(1, deleted.GetProperty("succeeded").GetInt64());
        await this.AssertMissingAsync("first", firstKey);
        await this.ReadItemAsync("second", secondKey);
        await this.ReadItemAsync("later", firstKey);
    }

    [Fact]
    public async Task Journal_FreshShellResume_RetriesFailedWritesWithoutReplayingSuccesses()
    {
        var token = TestContext.Current.CancellationToken;
        await this.container.CreateItemAsync(new { id = "counter", pk = "a", count = 0 }, cancellationToken: token);
        await this.container.CreateItemAsync(new { id = "conflict", pk = "b", marker = "existing" }, cancellationToken: token);
        var plan = await this.WriteOperationsAsync(
            new { op = "patch", id = "counter", partitionKey = "a", operations = new[] { new { op = "incr", path = "/count", value = 1 } } },
            new { op = "create", item = new { id = "conflict", pk = "b", marker = "retried" } },
            new { op = "create", item = new { id = "after", pk = "c", marker = "independent" } });
        var journal = this.PathFor("retry.journal");

        var (_, first) = await this.RunJsonAsync($"bulk run '{plan}' --journal '{journal}' --concurrency 1 --continue-on-error --yes", expectError: true);
        Assert.Equal(3, first.GetProperty("attempted").GetInt64());
        Assert.Equal(2, first.GetProperty("succeeded").GetInt64());
        Assert.Equal(1, first.GetProperty("failed").GetInt64());
        Assert.Equal(409, Assert.Single(first.GetProperty("errors").EnumerateArray()).GetProperty("statusCode").GetInt32());
        var counterBefore = await this.ReadItemAsync("counter", new PartitionKey("a"));
        var afterBefore = await this.ReadItemAsync("after", new PartitionKey("c"));
        var records = await ReadJsonLinesAsync(journal);
        Assert.Equal(7, records.Length);
        Assert.Equal(2, records[0].GetProperty("version").GetInt32());
        Assert.Equal(CosmosClient.Endpoint.ToString(), records[0].GetProperty("endpoint").GetString());
        Assert.Equal(await this.ReadContainerRidAsync(), records[0].GetProperty("containerRid").GetString());
        Assert.Equal(3, records[0].GetProperty("operationCount").GetInt64());
        Assert.Equal(64, records[0].GetProperty("hash").GetString()!.Length);
        foreach (var record in records.Skip(1))
        {
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.GetProperty("outcome").GetRawText())));
            Assert.Equal(checksum, record.GetProperty("checksum").GetString());
        }

        Assert.Equal(["succeeded", "failed", "succeeded"], records.Skip(1)
            .Select(record => Assert.IsType<string>(record.GetProperty("outcome").GetProperty("status").GetString()))
            .Where(status => status != "started").ToArray());
        await this.container.DeleteItemAsync<object>("conflict", new PartitionKey("b"), cancellationToken: token);
        using var resumedShell = ShellInterpreter.CreateInstance();
        await this.ConnectAndNavigateAsync(resumedShell);

        var (_, resumed) = await this.RunJsonAsync($"bulk run '{plan}' --journal '{journal}' --yes", shell: resumedShell);
        Assert.Equal(1, resumed.GetProperty("attempted").GetInt64());
        Assert.Equal(1, resumed.GetProperty("succeeded").GetInt64());
        Assert.Equal(2, resumed.GetProperty("skipped").GetInt64());
        Assert.Equal("retried", (await this.ReadItemAsync("conflict", new PartitionKey("b"))).GetProperty("marker").GetString());
        Assert.Equal(counterBefore.GetProperty("_etag").GetString(), (await this.ReadItemAsync("counter", new PartitionKey("a"))).GetProperty("_etag").GetString());
        Assert.Equal(afterBefore.GetProperty("_etag").GetString(), (await this.ReadItemAsync("after", new PartitionKey("c"))).GetProperty("_etag").GetString());
        Assert.Equal(1, (await this.ReadItemAsync("counter", new PartitionKey("a"))).GetProperty("count").GetInt32());
        Assert.Equal(9, (await ReadJsonLinesAsync(journal)).Length);
        var completedJournal = await File.ReadAllBytesAsync(journal, token);

        var (_, replay) = await this.RunJsonAsync($"bulk run '{plan}' --journal '{journal}' --max-ru 0.01 --yes", shell: resumedShell);
        Assert.True(replay.GetProperty("requestCharge").GetDouble() > 0.01);
        Assert.True(replay.GetProperty("success").GetBoolean());
        Assert.False(replay.GetProperty("budgetExceeded").GetBoolean());
        Assert.Equal(0, replay.GetProperty("attempted").GetInt64());
        Assert.Equal(3, replay.GetProperty("skipped").GetInt64());
        Assert.Equal(completedJournal, await File.ReadAllBytesAsync(journal, token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Journal_InterruptedWrite_HoldsBackDependentOperationsUntilExplicitRetry(bool tornOutcome)
    {
        var token = TestContext.Current.CancellationToken;
        await this.container.CreateItemAsync(new { id = "counter", pk = 7, count = 0 }, cancellationToken: token);
        var plan = await this.WriteOperationsAsync(
            new { op = "patch", id = "counter", partitionKey = 7, operations = new[] { new { op = "incr", path = "/count", value = 1 } } },
            new { op = "patch", id = "counter", partitionKey = 7.0, operations = new[] { new { op = "incr", path = "/count", value = 1 } } },
            new { op = "create", item = new { id = "independent", pk = "b" } });
        var journal = this.PathFor("interrupted.journal");
        var (_, preview) = await this.RunJsonAsync($"bulk run '{plan}' --dry-run");
        var budget = (preview.GetProperty("requestCharge").GetDouble() + 0.01).ToString("R", CultureInfo.InvariantCulture);

        var (stoppedState, stopped) = await this.RunJsonAsync($"bulk run '{plan}' --journal '{journal}' --concurrency 1 --max-ru {budget} --yes", expectError: true);
        Assert.Equal(ShellExitCode.Throttled, stoppedState.ExitCode);
        Assert.True(stopped.GetProperty("budgetExceeded").GetBoolean());
        Assert.True(stopped.GetProperty("resultIncomplete").GetBoolean());
        Assert.Equal(1, stopped.GetProperty("succeeded").GetInt64());
        Assert.Equal(1, stopped.GetProperty("attempted").GetInt64());
        var lines = await File.ReadAllLinesAsync(journal, token);
        Assert.Equal(3, lines.Length);
        Assert.Equal("started", JsonSerializer.Deserialize<JsonElement>(lines[1]).GetProperty("outcome").GetProperty("status").GetString());
        Assert.Equal("succeeded", JsonSerializer.Deserialize<JsonElement>(lines[2]).GetProperty("outcome").GetProperty("status").GetString());
        Assert.Equal(1, (await this.ReadItemAsync("counter", new PartitionKey(7))).GetProperty("count").GetInt32());
        await this.AssertMissingAsync("independent", new PartitionKey("b"));

        // Reproduce a crash after the service applied the write but before its outcome was durable.
        var interrupted = string.Join("\n", lines.Take(2)) + "\n" + (tornOutcome ? "{\"outcome\":" : string.Empty);
        await File.WriteAllTextAsync(journal, interrupted, token);
        using var resumedShell = ShellInterpreter.CreateInstance();
        await this.ConnectAndNavigateAsync(resumedShell);
        var (_, held) = await this.RunJsonAsync($"bulk run '{plan}' --journal '{journal}' --continue-on-error --yes", expectError: true, shell: resumedShell);
        Assert.Equal(1, held.GetProperty("attempted").GetInt64());
        Assert.Equal(1, held.GetProperty("succeeded").GetInt64());
        Assert.Equal(2, held.GetProperty("skipped").GetInt64());
        Assert.Equal(1, held.GetProperty("uncertain").GetInt64());
        Assert.True(held.GetProperty("resultIncomplete").GetBoolean());
        Assert.Equal(1, (await this.ReadItemAsync("counter", new PartitionKey(7))).GetProperty("count").GetInt32());
        await this.ReadItemAsync("independent", new PartitionKey("b"));
        var recovered = await ReadJsonLinesAsync(journal);
        Assert.Equal(4, recovered.Length);
        Assert.Equal("started", recovered[1].GetProperty("outcome").GetProperty("status").GetString());

        var (_, retried) = await this.RunJsonAsync($"bulk run '{plan}' --journal '{journal}' --retry-uncertain --yes", shell: resumedShell);
        Assert.Equal(2, retried.GetProperty("attempted").GetInt64());
        Assert.Equal(2, retried.GetProperty("succeeded").GetInt64());
        Assert.Equal(1, retried.GetProperty("skipped").GetInt64());
        Assert.Equal(0, retried.GetProperty("uncertain").GetInt64());
        Assert.Equal(3, (await this.ReadItemAsync("counter", new PartitionKey(7))).GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("operations")]
    [InlineData("container")]
    [InlineData("checksum")]
    public async Task Journal_ChangedIdentityOrCorruption_RejectsResumeWithoutWrites(string change)
    {
        var token = TestContext.Current.CancellationToken;
        await this.container.CreateItemAsync(new { id = "counter", pk = "a", count = 0 }, cancellationToken: token);
        var operation = new { op = "patch", id = "counter", partitionKey = "a", operations = new[] { new { op = "incr", path = "/count", value = 1 } } };
        var plan = await this.WriteOperationsAsync(operation);
        var journal = this.PathFor("identity.journal");
        await this.RunJsonAsync($"bulk run '{plan}' --journal '{journal}' --yes");
        var messageKey = "command-bulk-error-journal_mismatch";
        if (change == "operations")
        {
            await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(new
            {
                op = "patch",
                id = "counter",
                partitionKey = "a",
                operations = new[] { new { op = "incr", path = "/count", value = 2 } },
            }) + "\n", token);
        }
        else if (change == "container")
        {
            var oldRid = await this.ReadContainerRidAsync();
            await this.container.DeleteContainerAsync(cancellationToken: token);
            this.container = (await this.database.CreateContainerAsync("items", "/pk", cancellationToken: token)).Container;
            Assert.NotEqual(oldRid, await this.ReadContainerRidAsync());
            await this.container.CreateItemAsync(new { id = "counter", pk = "a", count = 20 }, cancellationToken: token);
        }
        else
        {
            var lines = await File.ReadAllLinesAsync(journal, token);
            var record = JsonNode.Parse(lines[2])!;
            record["outcome"]!["id"] = "tampered";
            lines[2] = record.ToJsonString();
            await File.WriteAllTextAsync(journal, string.Join("\n", lines) + "\n", token);
            messageKey = "command-bulk-error-journal_invalid";
        }

        var before = await this.ReadItemAsync("counter", new PartitionKey("a"));
        var journalBefore = await File.ReadAllBytesAsync(journal, token);
        using var resumedShell = ShellInterpreter.CreateInstance();
        await this.ConnectAndNavigateAsync(resumedShell);
        var state = await resumedShell.ExecuteCommandAsync($"bulk run '{plan}' --journal '{journal}' --yes", token);
        Assert.True(state.IsError);
        Assert.Contains(MessageService.GetString(messageKey), GetErrorMessage(state));
        var after = await this.ReadItemAsync("counter", new PartitionKey("a"));
        Assert.Equal(before.GetProperty("count").GetInt32(), after.GetProperty("count").GetInt32());
        Assert.Equal(before.GetProperty("_etag").GetString(), after.GetProperty("_etag").GetString());
        Assert.Equal(journalBefore, await File.ReadAllBytesAsync(journal, token));
    }

    [Fact]
    public async Task StatefulJournal_PartialFailure_RetainsJobAndOriginalJournalUntilRetryCompletes()
    {
        var token = TestContext.Current.CancellationToken;
        await this.container.CreateItemAsync(new { id = "counter", pk = "a", count = 0 }, cancellationToken: token);
        Assert.False((await ExecuteAsync("bulk begin --partition-key a")).IsError);
        Assert.False((await ExecuteAsync(
            """bulk add '[{"op":"patch","id":"counter","operations":[{"op":"incr","path":"/count","value":1}]},{"op":"delete","id":"missing"}]'""")).IsError);
        var journal = this.PathFor("stateful.journal");
        var (_, first) = await this.RunJsonAsync($"bulk execute --journal '{journal}' --concurrency 1 --continue-on-error --yes", expectError: true);
        Assert.Equal(1, first.GetProperty("succeeded").GetInt64());
        Assert.Equal(1, first.GetProperty("failed").GetInt64());
        Assert.Equal(404, Assert.Single(first.GetProperty("errors").EnumerateArray()).GetProperty("statusCode").GetInt32());
        var (_, status) = await this.RunJsonAsync("bulk status");
        Assert.True(status.GetProperty("active").GetBoolean());
        Assert.Equal(1, status.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, status.GetProperty("failed").GetInt32());
        var before = await this.ReadItemAsync("counter", new PartitionKey("a"));
        var journalBefore = await File.ReadAllBytesAsync(journal, token);

        var omitted = await ExecuteAsync("bulk execute --yes");
        Assert.True(omitted.IsError);
        Assert.Contains("same journal choice", GetErrorMessage(omitted));
        var replacement = this.PathFor("replacement.journal");
        var changed = await ExecuteAsync($"bulk execute --journal '{replacement}' --yes");
        Assert.True(changed.IsError);
        Assert.Contains("same journal choice", GetErrorMessage(changed));
        Assert.False(File.Exists(replacement));
        Assert.Equal(journalBefore, await File.ReadAllBytesAsync(journal, token));
        await this.container.CreateItemAsync(new { id = "missing", pk = "a" }, cancellationToken: token);

        var (_, completed) = await this.RunJsonAsync($"bulk commit --journal '{journal}' --yes");
        Assert.Equal(1, completed.GetProperty("attempted").GetInt64());
        Assert.Equal(1, completed.GetProperty("succeeded").GetInt64());
        Assert.Equal(1, completed.GetProperty("skipped").GetInt64());
        Assert.Equal(before.GetProperty("_etag").GetString(), (await this.ReadItemAsync("counter", new PartitionKey("a"))).GetProperty("_etag").GetString());
        await this.AssertMissingAsync("missing", new PartitionKey("a"));
        var (_, inactive) = await this.RunJsonAsync("bulk status");
        Assert.False(inactive.GetProperty("active").GetBoolean());
    }

    private static async Task<JsonElement[]> ReadJsonLinesAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        return lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
    }

    private string PathFor(string name)
    {
        var path = Path.Join(Path.GetTempPath(), $"bulk-integration-{Guid.NewGuid():N}-{Path.GetFileName(name)}");
        this.files.Add(path);
        return path;
    }

    private async Task<string> WriteOperationsAsync(params object[] operations)
    {
        var path = this.PathFor("operations.jsonl");
        await File.WriteAllTextAsync(path, string.Join("\n", operations.Select(operation => JsonSerializer.Serialize(operation))) + "\n", TestContext.Current.CancellationToken);
        return path;
    }

    private async Task ConnectAndNavigateAsync(ShellInterpreter shell)
    {
        await shell.ConnectAsync(ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint), null);
        shell.IsInteractiveSession = () => false;
        await this.NavigateAsync(shell);
    }

    private async Task NavigateAsync(ShellInterpreter shell)
    {
        var state = await shell.ExecuteCommandAsync($"cd /{this.database.Id}/{this.container.Id}", TestContext.Current.CancellationToken);
        Assert.False(state.IsError, FormatError(state));
    }

    private async Task<(CommandState State, JsonElement Result)> RunJsonAsync(string command, bool expectError = false, ShellInterpreter? shell = null)
    {
        var interpreter = shell ?? Shell;
        var output = this.PathFor("output.json");
        interpreter.StdOutRedirect = output;
        try
        {
            var state = await interpreter.ExecuteCommandAsync(command, TestContext.Current.CancellationToken);
            Assert.True(state.IsError == expectError, FormatError(state));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
            return (state, document.RootElement.Clone());
        }
        finally
        {
            interpreter.StdOutRedirect = null;
        }
    }

    private async Task<JsonElement> ReadItemAsync(string id, PartitionKey key)
    {
        using var response = await this.container.ReadItemStreamAsync(id, key, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(response.Content, cancellationToken: TestContext.Current.CancellationToken);
        return document.RootElement.Clone();
    }

    private async Task<string> ReadContainerRidAsync()
    {
        using var response = await this.container.ReadContainerStreamAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(response.Content, cancellationToken: TestContext.Current.CancellationToken);
        return Assert.IsType<string>(document.RootElement.GetProperty("_rid").GetString());
    }

    private async Task AssertMissingAsync(string id, PartitionKey key)
    {
        using var response = await this.container.ReadItemStreamAsync(id, key, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
