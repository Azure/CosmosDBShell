// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using NSubstitute;

public class BulkCommandTests
{
    private static readonly string[] SinglePath = ["/pk"];

    private static readonly string[] HierarchicalPaths = ["/tenant/id", "/region"];

    // Normalization: batch schema plus per-operation partition keys.
    [Fact]
    public void NormalizeAcceptsEveryBatchOperationKind()
    {
        string[] operations =
        [
            """{"op":"create","item":{"id":"1","pk":"a"}}""",
            """{"op":"upsert","item":{"id":"2","pk":"a"}}""",
            """{"op":"replace","id":"3","item":{"id":"3","pk":"a"},"ifMatch":"tag"}""",
            """{"op":"delete","id":"4","partitionKey":"a"}""",
            """{"op":"patch","id":"5","partitionKey":"a","operations":[{"op":"set","path":"/v","value":2}]}""",
        ];

        var normalized = operations.Select((json, index) => BulkOperationNormalizer.Normalize(Json(json), SinglePath, null, index)).ToList();

        Assert.Equal(["create", "upsert", "replace", "delete", "patch"], normalized.Select(operation => operation.Op));
        Assert.All(normalized, operation => Assert.Equal("""["a"]""", operation.PartitionKey));
        Assert.Contains("\"ifMatch\":\"tag\"", normalized[2].Json);
    }

    [Fact]
    public void NormalizeIsIdempotentSoSavedPlansHashIdentically()
    {
        var first = BulkOperationNormalizer.Normalize(
            Json("""{ "operations": [ {"op":"incr","path":"/n","value":1.50} ], "partitionKey": ["t", 4], "id":"x", "op":"patch" }"""),
            HierarchicalPaths,
            null,
            0);
        var second = BulkOperationNormalizer.Normalize(Json(first.Json), HierarchicalPaths, null, 0);

        Assert.Equal(first.Json, second.Json);
        Assert.Equal(BulkSpool.ComputeHash([first.Json]), BulkSpool.ComputeHash([second.Json]));
        Assert.Contains("1.50", first.Json);
    }

    [Fact]
    public void ItemOperationsDeriveHierarchicalKeysPreservingTypesAndUndefined()
    {
        var operation = BulkOperationNormalizer.Normalize(Json("""{"op":"upsert","item":{"id":"a","tenant":{"id":"42"}}}"""), HierarchicalPaths, null, 0);
        Assert.Equal("""["42",{}]""", operation.PartitionKey);
        Assert.Equal(
            new PartitionKeyBuilder().Add("42").AddNoneType().Build(),
            BulkOperationNormalizer.ParseKey(operation.PartitionKey));

        var nullKey = BulkOperationNormalizer.Normalize(Json("""{"op":"upsert","item":{"id":"a","pk":null}}"""), SinglePath, null, 0);
        Assert.Equal(PartitionKey.Null, BulkOperationNormalizer.ParseKey(nullKey.PartitionKey));
        var missingKey = BulkOperationNormalizer.Normalize(Json("""{"op":"upsert","item":{"id":"a"}}"""), SinglePath, null, 0);
        Assert.Equal(PartitionKey.None, BulkOperationNormalizer.ParseKey(missingKey.PartitionKey));
    }

    [Fact]
    public void DefaultPartitionKeyAppliesToAddressedOperationsAndIsCheckedForItems()
    {
        var defaultKey = BulkOperationNormalizer.CanonicalKeyFromArgument("tenant", 1);
        var delete = BulkOperationNormalizer.Normalize(Json("""{"op":"delete","id":"1"}"""), SinglePath, defaultKey, 0);
        Assert.Equal("""["tenant"]""", delete.PartitionKey);

        var explicitKey = BulkOperationNormalizer.Normalize(Json("""{"op":"delete","id":"1","partitionKey":"other"}"""), SinglePath, defaultKey, 0);
        Assert.Equal("""["other"]""", explicitKey.PartitionKey);

        var error = Assert.Throws<CommandException>(() =>
            BulkOperationNormalizer.Normalize(Json("""{"op":"upsert","item":{"id":"1","pk":"other"}}"""), SinglePath, defaultKey, 6));
        Assert.StartsWith("Operation 7:", error.Message);
        BulkOperationNormalizer.Normalize(Json("""{"op":"upsert","item":{"id":"1","pk":1}}"""), SinglePath, BulkOperationNormalizer.CanonicalKeyFromArgument("1.0", 1), 0);
    }

    [Theory]
    [InlineData("""{"op":"delete","id":"1"}""")]
    [InlineData("""{"op":"delete","id":"1","partitionKey":["a"]}""")]
    [InlineData("""{"op":"delete","id":"1","partitionKey":[{"x":1},"a"]}""")]
    [InlineData("""{"op":"delete","id":1,"partitionKey":["a","b"]}""")]
    [InlineData("""{"op":"create","item":{"id":"1"},"ifMatch":"tag"}""")]
    [InlineData("""{"op":"replace","id":"2","item":{"id":"1"}}""")]
    [InlineData("""{"op":"patch","id":"1","partitionKey":["a","b"],"operations":[{"op":"remove","path":"old"}]}""")]
    [InlineData("""{"op":"patch","id":"1","partitionKey":["a","b"],"operations":[]}""")]
    [InlineData("""{"op":"unknown","id":"1","partitionKey":["a","b"]}""")]
    public void InvalidOperationsAreRejected(string json)
    {
        Assert.Throws<CommandException>(() => BulkOperationNormalizer.Normalize(Json(json), HierarchicalPaths, null, 0));
    }

    [Fact]
    public void PatchSupportsAtMostTenOperations()
    {
        var ten = JsonSerializer.SerializeToElement(Enumerable.Repeat(new { op = "remove", path = "/old" }, 10));
        BulkOperationNormalizer.ValidatePatchOperations(ten);
        var eleven = JsonSerializer.SerializeToElement(Enumerable.Repeat(new { op = "remove", path = "/old" }, 11));
        Assert.Throws<CommandException>(() => BulkOperationNormalizer.ValidatePatchOperations(eleven));
    }

    // Option validation mirrors the per-subcommand surface.
    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public void InvalidSubcommandsAreRejected(string subcommand)
    {
        Assert.Throws<CommandException>(() => new BulkCommand { Subcommand = subcommand }.Validate(BulkCommand.NormalizeSubcommand(subcommand)));
    }

    [Fact]
    public void SubcommandAliasesMatchBatch()
    {
        Assert.Equal("execute", BulkCommand.NormalizeSubcommand("EXEC"));
        Assert.Equal("execute", BulkCommand.NormalizeSubcommand("commit"));
        Assert.Equal("cancel", BulkCommand.NormalizeSubcommand("abort"));
    }

    public static TheoryData<int> InvalidOptionCombinationIndexes => new(Enumerable.Range(0, InvalidOptionCombinations.Length));

    private static (string Subcommand, BulkCommand Command)[] InvalidOptionCombinations =>
    [
        ("run", new BulkCommand { Where = "true" }),
        ("run", new BulkCommand { ETag = true }),
        ("run", new BulkCommand { Save = "plan.jsonl" }),
        ("run", new BulkCommand { RetryUncertain = true }),
        ("run", new BulkCommand { DryRun = true, Journal = "journal.jsonl" }),
        ("patch", new BulkCommand { Where = "true" }),
        ("patch", new BulkCommand { Where = "true", Operations = "not json" }),
        ("patch", new BulkCommand { Where = "true", Operations = """[{"op":"set","path":"/v"}]""" }),
        ("delete", new BulkCommand { Data = "SELECT * FROM c" }),
        ("delete", new BulkCommand { Where = "true", Operations = "[]" }),
        ("delete", new BulkCommand { Where = "true", Journal = "journal.jsonl" }),
        ("delete", new BulkCommand { Where = " " }),
        ("begin", new BulkCommand { Yes = true }),
        ("execute", new BulkCommand { PartitionKeyArgument = "a" }),
        ("status", new BulkCommand { Database = "db" }),
        ("run", new BulkCommand { Concurrency = 0 }),
        ("delete", new BulkCommand { Where = "true", MaxItems = 0 }),
        ("run", new BulkCommand { MaxRu = double.NaN }),
        ("run", new BulkCommand { MaxRu = -1 }),
    ];

    [Theory]
    [MemberData(nameof(InvalidOptionCombinationIndexes))]
    public void InvalidOptionCombinationsAreRejected(int index)
    {
        var (subcommand, command) = InvalidOptionCombinations[index];
        Assert.Throws<CommandException>(() => command.Validate(subcommand));
    }

    [Fact]
    public void SelectionQueryProjectsEscapedPartitionKeyPaths()
    {
        var query = BulkCommand.BuildSelectionQuery("c.expired = true", ["/tenant/id", "/re\"gion"]);
        Assert.Equal("""SELECT c.id, c._etag, c["tenant"]["id"] AS __pk0, c["re\"gion"] AS __pk1 FROM c WHERE (c.expired = true)""", query);
    }

    // Writes.
    [Theory]
    [InlineData("create")]
    [InlineData("upsert")]
    [InlineData("replace")]
    [InlineData("delete")]
    [InlineData("patch")]
    public async Task WriteUsesMatchingApiWithKeyIfMatchAndNoContentResponse(string op)
    {
        var container = Substitute.For<Container>();
        Fixture.ConfigureWrites(container, HttpStatusCode.OK, 3.5);
        var json = op switch
        {
            "create" => """{"op":"create","item":{"id":"1","pk":"a"}}""",
            "upsert" or "replace" => $$"""{"op":"{{op}}","item":{"id":"1","pk":"a"},"ifMatch":"tag"}""",
            "delete" => """{"op":"delete","id":"1","partitionKey":"a","ifMatch":"tag"}""",
            _ => """{"op":"patch","id":"1","partitionKey":"a","ifMatch":"tag","operations":[{"op":"set","path":"/v","value":2}]}""",
        };
        var operation = BulkOperationNormalizer.Normalize(Json(json), SinglePath, null, 0);

        var result = await BulkCommand.WriteAsync(container, operation, TestContext.Current.CancellationToken);

        Assert.Equal(BulkOutcome.Succeeded, result.Status);
        Assert.Equal(3.5, result.RequestCharge);
        var call = Assert.Single(container.ReceivedCalls());
        Assert.StartsWith(char.ToUpperInvariant(op[0]) + op[1..], call.GetMethodInfo().Name);
        var arguments = call.GetArguments();
        Assert.Contains(arguments, argument => argument is PartitionKey key && key.Equals(new PartitionKey("a")));
        var options = Assert.Single(arguments.OfType<ItemRequestOptions>());
        Assert.False(options.EnableContentResponseOnWrite);
        Assert.Equal(op == "create" ? null : "tag", options.IfMatchEtag);
    }

    [Theory]
    [InlineData(404, BulkOutcome.Failed)]
    [InlineData(412, BulkOutcome.Failed)]
    [InlineData(429, BulkOutcome.Failed)]
    [InlineData(408, BulkOutcome.Uncertain)]
    [InlineData(503, BulkOutcome.Uncertain)]
    public async Task WriteClassifiesServiceErrors(int status, string expected)
    {
        var container = Substitute.For<Container>();
        container.DeleteItemAsync<JsonElement>(default!, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs<Task<ItemResponse<JsonElement>>>(_ => throw new CosmosException("failure", (HttpStatusCode)status, 0, "activity", 2));

        var result = await BulkCommand.WriteAsync(container, Operation(0), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(2, result.RequestCharge);
    }

    // Execution engine.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public async Task ConcurrencyRefillsWhicheverSlotFinishesAndDrains(int concurrency)
    {
        using var timeout = Timeout();
        var gates = Enumerable.Range(0, concurrency + 2).Select(_ => new TaskCompletionSource<BulkOutcome>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var started = Enumerable.Range(0, gates.Length).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var summary = new BulkSummary();
        var run = BulkExecutor.ExecuteAsync(
            SourceAsync(gates.Length, timeout.Token),
            (operation, _) =>
            {
                started[(int)operation.Index].SetResult();
                return gates[(int)operation.Index].Task;
            },
            IgnoreAsync,
            new Dictionary<long, BulkOutcome>(),
            summary,
            concurrency,
            null,
            false,
            false,
            timeout.Token);
        try
        {
            await Task.WhenAll(started.Take(concurrency).Select(gate => gate.Task)).WaitAsync(timeout.Token);
            Assert.False(started[concurrency].Task.IsCompleted);
            gates[concurrency - 1].SetResult(Succeeded(concurrency - 1));
            await started[concurrency].Task.WaitAsync(timeout.Token);
            Assert.False(started[concurrency + 1].Task.IsCompleted);
            Assert.False(run.IsCompleted);
        }
        finally
        {
            for (var i = 0; i < gates.Length; i++)
            {
                gates[i].TrySetResult(Succeeded(i));
            }
        }

        await run.WaitAsync(timeout.Token);
        Assert.Equal(gates.Length, summary.Attempted);
        Assert.Equal(gates.Length, summary.Succeeded);
        Assert.Equal(gates.Length, summary.RequestCharge);
        Assert.True(summary.Success);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 5)]
    public async Task FailureStopsOrContinues(bool continueOnError, int attempted)
    {
        var summary = new BulkSummary();
        await BulkExecutor.ExecuteAsync(
            SourceAsync(5, TestContext.Current.CancellationToken),
            (operation, _) => Task.FromResult(Succeeded(operation.Index) with { Status = BulkOutcome.Failed, StatusCode = 409 }),
            IgnoreAsync,
            new Dictionary<long, BulkOutcome>(),
            summary,
            1,
            null,
            continueOnError,
            false,
            TestContext.Current.CancellationToken);

        Assert.Equal(attempted, summary.Attempted);
        Assert.Equal(attempted, summary.Failed);
        Assert.Equal(!continueOnError, summary.ResultIncomplete);
        Assert.False(summary.Success);
    }

    [Fact]
    public async Task RuBudgetStopsNewWrites()
    {
        var summary = new BulkSummary { RequestCharge = 2 };
        await BulkExecutor.ExecuteAsync(
            SourceAsync(5, TestContext.Current.CancellationToken),
            (operation, _) => Task.FromResult(Succeeded(operation.Index)),
            IgnoreAsync,
            new Dictionary<long, BulkOutcome>(),
            summary,
            1,
            4,
            true,
            false,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, summary.Attempted);
        Assert.True(summary.BudgetExceeded);
        Assert.True(summary.ResultIncomplete);
    }

    [Fact]
    public async Task OperationsOnTheSameItemRunInOrderEvenWithEquivalentKeys()
    {
        using var timeout = Timeout();
        var gate = new TaskCompletionSource<BulkOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new List<long>();
        async IAsyncEnumerable<BulkOperation> OperationsAsync()
        {
            yield return new BulkOperation(0, "patch", "same", "[1]", "{}");
            yield return new BulkOperation(1, "patch", "same", "[1.0]", "{}");
            await Task.CompletedTask;
        }

        var run = BulkExecutor.ExecuteAsync(
            OperationsAsync(),
            (operation, _) =>
            {
                started.Add(operation.Index);
                return operation.Index == 0 ? gate.Task : Task.FromResult(Succeeded(1));
            },
            IgnoreAsync,
            new Dictionary<long, BulkOutcome>(),
            new BulkSummary(),
            16,
            null,
            true,
            false,
            timeout.Token);

        Assert.Equal([0L], started);
        gate.SetResult(Succeeded(0));
        await run.WaitAsync(timeout.Token);
        Assert.Equal([0L, 1L], started);
    }

    [Fact]
    public async Task CancellationDrainsInFlightWritesAndRecordsUncertainOutcome()
    {
        using var cancellation = Timeout();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var records = new List<BulkOutcome>();
        var summary = new BulkSummary();
        var run = BulkExecutor.ExecuteAsync(
            SourceAsync(3, cancellation.Token),
            async (operation, token) =>
            {
                started.TrySetResult();
                await Task.Delay(System.Threading.Timeout.Infinite, token);
                return Succeeded(operation.Index);
            },
            outcome =>
            {
                records.Add(outcome);
                return Task.CompletedTask;
            },
            new Dictionary<long, BulkOutcome>(),
            summary,
            1,
            null,
            true,
            false,
            cancellation.Token);

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, summary.Attempted);
        Assert.Equal(1, summary.Uncertain);
        Assert.Equal([BulkOutcome.Started, BulkOutcome.Uncertain], records.Select(record => record.Status));
    }

    [Fact]
    public async Task UnexpectedFailureDrainsOtherWritesAndKeepsOriginalException()
    {
        using var timeout = Timeout();
        var first = new TaskCompletionSource<BulkOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<BulkOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var summary = new BulkSummary();
        var run = BulkExecutor.ExecuteAsync(
            SourceAsync(2, timeout.Token),
            (operation, _) => operation.Index == 0 ? first.Task : second.Task,
            IgnoreAsync,
            new Dictionary<long, BulkOutcome>(),
            summary,
            2,
            null,
            true,
            false,
            timeout.Token);
        var expected = new IOException("unexpected");
        first.SetException(expected);
        Assert.False(run.IsCompleted);
        second.SetResult(Succeeded(1));

        var actual = await Assert.ThrowsAsync<IOException>(async () => await run.WaitAsync(timeout.Token));
        Assert.Same(expected, actual);
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Uncertain);
    }

    [Fact]
    public async Task ResumeSkipsSucceededRetriesFailedAndHoldsBackUncertain()
    {
        var previous = new Dictionary<long, BulkOutcome>
        {
            [0] = Succeeded(0),
            [1] = Succeeded(1) with { Status = BulkOutcome.Failed, StatusCode = 429 },
            [2] = Succeeded(2) with { Status = BulkOutcome.Started },
            [3] = Succeeded(3) with { Status = BulkOutcome.Uncertain },
        };
        var attempted = new List<long>();
        var summary = new BulkSummary();
        await BulkExecutor.ExecuteAsync(
            SourceAsync(5, TestContext.Current.CancellationToken),
            (operation, _) =>
            {
                attempted.Add(operation.Index);
                return Task.FromResult(Succeeded(operation.Index));
            },
            IgnoreAsync,
            previous,
            summary,
            1,
            null,
            false,
            false,
            TestContext.Current.CancellationToken);

        Assert.Equal([1L, 4L], attempted);
        Assert.Equal(3, summary.Skipped);
        Assert.Equal(2, summary.Failed);
        Assert.Equal(2, summary.Uncertain);
        Assert.False(summary.Success);

        attempted.Clear();
        await BulkExecutor.ExecuteAsync(
            SourceAsync(5, TestContext.Current.CancellationToken),
            (operation, _) =>
            {
                attempted.Add(operation.Index);
                return Task.FromResult(Succeeded(operation.Index));
            },
            IgnoreAsync,
            previous,
            new BulkSummary(),
            1,
            null,
            false,
            true,
            TestContext.Current.CancellationToken);
        Assert.Equal([1L, 2L, 3L, 4L], attempted);
    }

    // Journal.
    [Fact]
    public async Task JournalPersistsOutcomesTrimsTornTailAndRejectsMismatchOrConcurrentUse()
    {
        using var directory = new TempDirectory();
        var path = directory.PathFor("journal.jsonl");
        using (var journal = BulkJournal.Open(path, "endpoint", "rid", "hash", 3))
        {
            await journal.RecordAsync(Succeeded(0) with { Status = BulkOutcome.Started });
            await journal.RecordAsync(Succeeded(0));
            await journal.RecordAsync(Succeeded(1) with { Status = BulkOutcome.Started });
            Assert.ThrowsAny<CommandException>(() => BulkJournal.Open(path, "endpoint", "rid", "hash", 3));
        }

        await File.AppendAllTextAsync(path, "{\"index\":2,\"op\":\"del", TestContext.Current.CancellationToken);
        using (var journal = BulkJournal.Open(path, "endpoint", "rid", "hash", 3))
        {
            Assert.Equal(BulkOutcome.Succeeded, journal.Previous[0].Status);
            Assert.Equal(BulkOutcome.Started, journal.Previous[1].Status);
            Assert.False(journal.Previous.ContainsKey(2));
            await journal.RecordAsync(Succeeded(2));
        }

        Assert.EndsWith("\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Throws<CommandException>(() => BulkJournal.Open(path, "endpoint", "rid", "other-hash", 3));
        Assert.Throws<CommandException>(() => BulkJournal.Open(path, "endpoint", "recreated", "hash", 3));

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        lines[1] = "not json";
        await File.WriteAllTextAsync(path, string.Join("\n", lines) + "\n", TestContext.Current.CancellationToken);
        Assert.Throws<CommandException>(() => BulkJournal.Open(path, "endpoint", "rid", "hash", 3));
    }

    // End-to-end command behavior against a substituted container.
    [Fact]
    public async Task RunExecutesMixedOperationsAcrossPartitions()
    {
        using var fixture = new Fixture();
        var state = await fixture.RunAsync(
            """bulk run '[{"op":"upsert","item":{"id":"1","pk":"a"}},{"op":"delete","id":"2","partitionKey":"b"},{"op":"patch","id":"3","partitionKey":"c","operations":[{"op":"set","path":"/v","value":2}]}]' --yes --db db --con items""");

        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.False(state.IsError);
        Assert.Equal(3, result.GetProperty("operationCount").GetInt64());
        Assert.Equal(3, result.GetProperty("succeeded").GetInt64());
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(17, state.RequestCharge);
    }

    [Fact]
    public async Task RunWithPipedOperationsAndDefaultPartitionKeyMatchesBatchStyle()
    {
        using var fixture = new Fixture();
        var state = await fixture.RunAsync("""echo '[{"op":"delete","id":"1"},{"op":"delete","id":"2"}]' | bulk run --partition-key a --yes --db db --con items""");

        Assert.False(state.IsError);
        await fixture.Container.Received(2).DeleteItemAsync<JsonElement>(Arg.Any<string>(), new PartitionKey("a"), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunReadsFilesAndValidatesEverythingBeforeWriting(bool array)
    {
        using var fixture = new Fixture();
        var path = fixture.Directory.PathFor("operations.json");
        string[] rows = ["""{"op":"upsert","item":{"id":"1","pk":"a"}}""", """{"op":"delete","id":"2"}"""];
        await File.WriteAllTextAsync(path, array ? "[" + string.Join(",", rows) + "]" : string.Join("\n", rows), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk run '{path}' --yes --db db --con items"));

        Assert.StartsWith("Operation 2:", exception.Message);
        Assert.Empty(fixture.Writes());
    }

    [Fact]
    public async Task NonInteractiveWritesRequireYesBeforeAnyRequest()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("""bulk run '[{"op":"delete","id":"1","partitionKey":"a"}]' --db db --con items"""));
        Assert.Empty(fixture.Container.ReceivedCalls());
    }

    [Fact]
    public async Task DryRunValidatesWithoutWriting()
    {
        using var fixture = new Fixture();
        var state = await fixture.RunAsync("""bulk run '[{"op":"delete","id":"1","partitionKey":"a"}]' --dry-run --db db --con items""");

        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.True(result.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, result.GetProperty("operationCount").GetInt64());
        Assert.Empty(fixture.Writes());
    }

    [Fact]
    public async Task WhereSelectionSavesPlanThatRunResumesWithJournal()
    {
        using var fixture = new Fixture();
        fixture.Query(
            """{"id":"a","_etag":"e1","__pk0":"tenant"}""",
            """{"id":"b","_etag":"e2"}""",
            """{"id":"c","_etag":"e3","__pk0":null}""");
        var plan = fixture.Directory.PathFor("plan.jsonl");
        var journal = fixture.Directory.PathFor("journal.jsonl");

        var preview = await fixture.RunAsync(
            $$"""bulk patch --where "c.v = 1" --operations '[{"op":"set","path":"/v","value":2}]' --etag --dry-run --save '{{plan}}' --db db --con items""");
        Assert.Equal(3, Assert.IsType<ShellJson>(preview.Result).Value.GetProperty("operationCount").GetInt64());
        Assert.Contains("c.v = 1", fixture.LastQuery);
        Assert.Empty(fixture.Writes());

        var saved = await File.ReadAllLinesAsync(plan, TestContext.Current.CancellationToken);
        Assert.Equal(3, saved.Length);
        Assert.Contains("\"partitionKey\":[{}]", saved[1]);
        Assert.Contains("\"partitionKey\":[null]", saved[2]);
        Assert.Contains("\"ifMatch\":\"e1\"", saved[0]);

        fixture.FailPatchFor("b", HttpStatusCode.TooManyRequests);
        var first = await fixture.RunAsync($"bulk run '{plan}' --journal '{journal}' --continue-on-error --yes --db db --con items");
        Assert.True(first.IsError);
        Assert.Equal(ShellExitCode.GeneralFailure, first.ExitCode);

        fixture.FailPatchFor(null, HttpStatusCode.OK);
        var resumed = await fixture.RunAsync($"bulk run '{plan}' --journal '{journal}' --yes --db db --con items");
        var result = Assert.IsType<ShellJson>(resumed.Result).Value;
        Assert.False(resumed.IsError);
        Assert.Equal(2, result.GetProperty("skipped").GetInt64());
        Assert.Equal(1, result.GetProperty("attempted").GetInt64());
        await fixture.Container.Received(4).PatchItemAsync<JsonElement>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<IReadOnlyList<PatchOperation>>(),
            Arg.Is<PatchItemRequestOptions>(options => options.IfMatchEtag != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhereSelectionHonorsMaxItemsAndBudget()
    {
        using var fixture = new Fixture();
        fixture.Query("""{"id":"a","__pk0":"t"}""", """{"id":"b","__pk0":"t"}""", """{"id":"c","__pk0":"t"}""");

        var limited = await fixture.RunAsync("""bulk delete --where "true" --max-items 2 --yes --db db --con items""");
        var result = Assert.IsType<ShellJson>(limited.Result).Value;
        Assert.True(result.GetProperty("selectionLimited").GetBoolean());
        Assert.Equal(2, result.GetProperty("succeeded").GetInt64());

        var save = fixture.Directory.PathFor("partial.jsonl");
        var budget = await fixture.RunAsync($"bulk delete --where \"true\" --max-ru 4 --save '{save}' --yes --db db --con items");
        Assert.True(budget.IsError);
        Assert.Equal(ShellExitCode.Throttled, budget.ExitCode);
        Assert.True(Assert.IsType<ShellJson>(budget.Result).Value.GetProperty("budgetExceeded").GetBoolean());
        Assert.False(File.Exists(save));
        Assert.Equal(2, fixture.Writes().Count());
    }

    [Fact]
    public async Task SaveNeverOverwritesExistingFiles()
    {
        using var fixture = new Fixture();
        var save = fixture.Directory.PathFor("existing.jsonl");
        await File.WriteAllTextAsync(save, "keep", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk delete --where \"true\" --dry-run --save '{save}' --db db --con items"));
        Assert.Equal("keep", await File.ReadAllTextAsync(save, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StatefulJobRetainsOutcomesAndRetriesOnlyFailedOperations()
    {
        using var fixture = new Fixture();
        await fixture.RunAsync("bulk begin --partition-key a --db db --con items");
        await fixture.RunAsync("""bulk add '{"op":"delete","id":"1"}'""");
        await fixture.RunAsync("""bulk add '[{"op":"patch","id":"2","operations":[{"op":"incr","path":"/n","value":1}]},{"op":"upsert","item":{"id":"3","pk":"a"}}]'""");
        Assert.Equal(3, fixture.Shell.CurrentBulk!.Operations.Count);

        var status = Assert.IsType<ShellJson>((await fixture.RunAsync("bulk status")).Result).Value;
        Assert.Equal(3, status.GetProperty("operationCount").GetInt32());
        var show = Assert.IsType<ShellJson>((await fixture.RunAsync("bulk show")).Result).Value;
        Assert.Equal("""["a"]""", show[0].GetProperty("partitionKey").GetRawText());

        fixture.FailPatchFor("2", HttpStatusCode.PreconditionFailed);
        var failed = await fixture.RunAsync("bulk execute --continue-on-error --yes");
        Assert.True(failed.IsError);
        Assert.NotNull(fixture.Shell.CurrentBulk);

        fixture.FailPatchFor(null, HttpStatusCode.OK);
        var retried = await fixture.RunAsync("bulk commit --yes");
        Assert.False(retried.IsError);
        Assert.Equal(2, Assert.IsType<ShellJson>(retried.Result).Value.GetProperty("skipped").GetInt64());
        Assert.Null(fixture.Shell.CurrentBulk);
        await fixture.Container.Received(1).DeleteItemAsync<JsonElement>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StatefulErrorsMatchBatch()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("""bulk add '{"op":"delete","id":"1","partitionKey":"a"}'"""));
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("bulk execute --yes"));
        await fixture.RunAsync("bulk begin --db db --con items");
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("bulk begin --db db --con items"));
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("""bulk add '{"op":"delete","id":"1"}'"""));
        Assert.Empty(fixture.Shell.CurrentBulk!.Operations);
        await fixture.RunAsync("bulk abort");
        Assert.Null(fixture.Shell.CurrentBulk);
    }

    [Fact]
    public async Task StatefulExecuteRejectsRecreatedContainer()
    {
        using var fixture = new Fixture();
        await fixture.RunAsync("bulk begin --db db --con items");
        await fixture.RunAsync("""bulk add '{"op":"delete","id":"1","partitionKey":"a"}'""");
        fixture.Rid = "recreated";

        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("bulk execute --yes"));
        Assert.Empty(fixture.Writes());
    }

    // MCP and help surface.
    [Fact]
    public void McpSchemaExposesSafeBoundsAndDescribesSubcommands()
    {
        var factory = new CommandRunner().Commands["bulk"];
        var tool = ToolOperations.GetTool(factory);
        var properties = tool.InputSchema.GetProperty("properties");

        Assert.Equal(16, properties.GetProperty("concurrency").GetProperty("default").GetInt32());
        Assert.Equal(1, properties.GetProperty("concurrency").GetProperty("minimum").GetInt32());
        Assert.Equal(1, properties.GetProperty("max-items").GetProperty("minimum").GetInt32());
        Assert.Equal(0, properties.GetProperty("max-ru").GetProperty("exclusiveMinimum").GetInt32());
        Assert.Contains("subcommand", tool.InputSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("patch --where", tool.Description);
        Assert.True(factory.McpAnnotation!.Confirmable);
    }

    [Fact]
    public void HelpExplainsMcpAvailability()
    {
        var state = HelpCommand.PrintCommandHelp("bulk", new CommandRunner(), plain: true);
        var help = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Contains("Available through MCP", help.GetProperty("isRestricted").GetString());
    }

    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);

    private static BulkOperation Operation(long index) =>
        new(index, "delete", index.ToString(CultureInfo.InvariantCulture), """["a"]""", $$"""{"op":"delete","id":"{{index}}","partitionKey":["a"]}""");

    private static BulkOutcome Succeeded(long index) => BulkOutcome.Create(Operation(index), BulkOutcome.Succeeded, 1, 200);

    private static Task IgnoreAsync(BulkOutcome outcome) => Task.CompletedTask;

    private static CancellationTokenSource Timeout()
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(10));
        return cancellation;
    }

    private static async IAsyncEnumerable<BulkOperation> SourceAsync(int count, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.CompletedTask;
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            yield return Operation(index);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = System.IO.Directory.CreateTempSubdirectory("cosmos-bulk-test-");

        public string PathFor(string name) => Path.Combine(this.directory.FullName, name);

        public void Dispose() => this.directory.Delete(recursive: true);
    }

    private sealed class Fixture : IDisposable
    {
        private string? failingPatchId;
        private HttpStatusCode failingStatus = HttpStatusCode.OK;

        public Fixture()
        {
            var client = Substitute.For<CosmosClient>();
            var database = Substitute.For<Database>();
            client.Endpoint.Returns(new Uri("https://unit-test.documents.azure.com"));
            client.GetDatabase("db").Returns(database);
            database.GetContainer("items").Returns(this.Container);
            this.Shell.State = new ConnectedState(client);
            this.Shell.IsInteractiveSession = () => false;
            this.Container.ReadContainerStreamAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                var response = new ResponseMessage(HttpStatusCode.OK)
                {
                    Content = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { _rid = this.Rid, partitionKey = new { paths = SinglePath } }))),
                };
                response.Headers.Add("x-ms-request-charge", "2");
                return Task.FromResult(response);
            });
            ConfigureWrites(this.Container, HttpStatusCode.OK, 5);
            this.Container.PatchItemAsync<JsonElement>(default!, default, default!, default, default).ReturnsForAnyArgs(call =>
            {
                if (call.ArgAt<string>(0) == this.failingPatchId)
                {
                    throw new CosmosException("failure", this.failingStatus, 0, "activity", 1);
                }

                return Task.FromResult(Response(HttpStatusCode.OK, 5));
            });
        }

        public ShellInterpreter Shell { get; } = ShellInterpreter.CreateInstance();

        public Container Container { get; } = Substitute.For<Container>();

        public TempDirectory Directory { get; } = new();

        public string Rid { get; set; } = "rid";

        public string? LastQuery { get; private set; }

        public static void ConfigureWrites(Container container, HttpStatusCode status, double charge)
        {
            var response = Response(status, charge);
            container.CreateItemAsync(default(JsonElement), default, default, default).ReturnsForAnyArgs(response);
            container.UpsertItemAsync(default(JsonElement), default, default, default).ReturnsForAnyArgs(response);
            container.ReplaceItemAsync(default(JsonElement), default!, default, default, default).ReturnsForAnyArgs(response);
            container.DeleteItemAsync<JsonElement>(default!, default, default, default).ReturnsForAnyArgs(response);
            container.PatchItemAsync<JsonElement>(default!, default, default!, default, default).ReturnsForAnyArgs(response);
        }

        public void FailPatchFor(string? id, HttpStatusCode status)
        {
            this.failingPatchId = id;
            this.failingStatus = status;
        }

        public IEnumerable<NSubstitute.Core.ICall> Writes() =>
            this.Container.ReceivedCalls().Where(call => call.GetMethodInfo().Name is "CreateItemAsync" or "UpsertItemAsync" or "ReplaceItemAsync" or "DeleteItemAsync" or "PatchItemAsync");

        public void Query(params string[] rows)
        {
            this.Container.GetItemQueryIterator<JsonElement>(Arg.Any<QueryDefinition>(), Arg.Any<string?>(), Arg.Any<QueryRequestOptions>()).Returns(call =>
            {
                this.LastQuery = call.ArgAt<QueryDefinition>(0).QueryText;
                var index = 0;
                var iterator = Substitute.For<FeedIterator<JsonElement>>();
                iterator.HasMoreResults.Returns(_ => index < rows.Length);
                iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(_ =>
                {
                    var row = Json(rows[index++]);
                    var page = Substitute.For<FeedResponse<JsonElement>>();
                    page.GetEnumerator().Returns(_ => ((IEnumerable<JsonElement>)[row]).GetEnumerator());
                    page.RequestCharge.Returns(1);
                    return Task.FromResult(page);
                });
                return iterator;
            });
        }

        public async Task<CommandState> RunAsync(string commandText)
        {
            var state = await this.Shell.RunCommandAsync(new CommandState(), commandText, TestContext.Current.CancellationToken);
            if (state is ErrorCommandState { Result: null } error)
            {
                throw error.Exception;
            }

            return state;
        }

        public void Dispose()
        {
            this.Shell.Dispose();
            this.Directory.Dispose();
        }

        private static ItemResponse<JsonElement> Response(HttpStatusCode status, double charge)
        {
            var response = Substitute.For<ItemResponse<JsonElement>>();
            response.StatusCode.Returns(status);
            response.RequestCharge.Returns(charge);
            return response;
        }
    }
}
