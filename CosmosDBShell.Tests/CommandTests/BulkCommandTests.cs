// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using Spectre.Console;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
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

    [Theory]
    [InlineData("create")]
    [InlineData("upsert")]
    [InlineData("replace")]
    public void ItemOperationsDecodeQuotedPartitionKeyPaths(string op)
    {
        var operation = BulkOperationNormalizer.Normalize(
            Json($$$$"""{"op":"{{{{op}}}}","item":{"id":"1","tenant id":"tenant","a/b":{"c":42}}}"""),
            ["/\"tenant id\"", "/\"a/b\"/c"],
            null,
            0);

        Assert.Equal("""["tenant",42]""", operation.PartitionKey);
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
    [InlineData("[\"tenant\"")]
    [InlineData("{\"pk\":")]
    [InlineData("\"tenant")]
    [InlineData("  [\"tenant\"")]
    [InlineData("  {\"pk\":")]
    [InlineData("  \"tenant")]
    public void DefaultPartitionKeyRejectsMalformedJsonShapes(string raw)
    {
        var error = Assert.Throws<CommandException>(() => BulkOperationNormalizer.CanonicalKeyFromArgument(raw, 1));

        Assert.IsType<JsonException>(error.InnerException);
    }

    [Theory]
    [InlineData("001", """["001"]""")]
    [InlineData("-svc", """["-svc"]""")]
    [InlineData("123abc", """["123abc"]""")]
    [InlineData("", """[""]""")]
    [InlineData("tenant", """["tenant"]""")]
    [InlineData("42", "[42]")]
    [InlineData("true", "[true]")]
    [InlineData("null", "[null]")]
    [InlineData("{}", "[{}]")]
    [InlineData("\"[tenant\"", """["[tenant"]""")]
    [InlineData("""["tenant","west"]""", """["tenant","west"]""")]
    public void DefaultPartitionKeyPreservesValidKeysAndAmbiguousScalars(string raw, string canonical)
    {
        Assert.Equal(canonical, BulkOperationNormalizer.CanonicalKeyFromArgument(raw, Json(canonical).GetArrayLength()));
    }

    [Theory]
    [InlineData(9007199254740993L)]
    [InlineData(-9007199254740993L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void NumericKeysMatchSdkIntegerConversionsAndCanonicalJsonKeepsOriginalDigits(long value)
    {
        var number = value.ToString(CultureInfo.InvariantCulture);
        var single = BulkOperationNormalizer.Normalize(Json($$"""{"op":"delete","id":"1","partitionKey":{{number}}}"""), SinglePath, null, 0);
        Assert.Equal($"[{number}]", single.PartitionKey);
        Assert.Equal(new PartitionKey(value), new PartitionKey((double)value));
        Assert.Equal(new PartitionKey(value).ToString(), BulkOperationNormalizer.ParseKey(single.PartitionKey).ToString());

        var hierarchical = BulkOperationNormalizer.Normalize(Json($$"""{"op":"delete","id":"1","partitionKey":[{{number}},"tenant"]}"""), HierarchicalPaths, null, 0);
        Assert.Equal($"""[{number},"tenant"]""", hierarchical.PartitionKey);
        Assert.Equal(
            new PartitionKeyBuilder().Add(value).Add("tenant").Build().ToString(),
            BulkOperationNormalizer.ParseKey(hierarchical.PartitionKey).ToString());

        var stringKey = BulkOperationNormalizer.CanonicalKeyFromArgument($"\"{number}\"", 1);
        Assert.Equal(new PartitionKey(number).ToString(), BulkOperationNormalizer.ParseKey(stringKey).ToString());
    }

    [Theory]
    [InlineData("""{"op":"delete","id":"1"}""")]
    [InlineData("""{"op":"delete","id":"1","partitionKey":["a"]}""")]
    [InlineData("""{"op":"delete","id":"1","partitionKey":[{"x":1},"a"]}""")]
    [InlineData("""{"op":"delete","id":1,"partitionKey":["a","b"]}""")]
    [InlineData("""{"op":"create","item":{"id":"1"},"ifMatch":"tag"}""")]
    [InlineData("""{"op":"upsert","item":{"id":"1"},"ifMatch":"tag"}""")]
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

    [Theory]
    [InlineData("dry-run", false)]
    [InlineData("dry-run", true)]
    [InlineData("yes", false)]
    [InlineData("yes", true)]
    [InlineData("continue-on-error", false)]
    [InlineData("continue-on-error", true)]
    [InlineData("etag", false)]
    [InlineData("etag", true)]
    [InlineData("retry-uncertain", false)]
    [InlineData("retry-uncertain", true)]
    public void ExplicitBooleanOptionsAreValidatedByPresence(string option, bool value)
    {
        var command = option switch
        {
            "dry-run" => new BulkCommand { DryRun = value },
            "yes" => new BulkCommand { Yes = value },
            "continue-on-error" => new BulkCommand { ContinueOnError = value },
            "etag" => new BulkCommand { ETag = value },
            _ => new BulkCommand { RetryUncertain = value },
        };

        var error = Assert.Throws<CommandException>(() => command.Validate("status"));

        Assert.Contains($"--{option}", error.Message);
    }

    [Fact]
    public void FalseBooleanOptionsAreAcceptedWhenSupported()
    {
        new BulkCommand { DryRun = false, Yes = false, ContinueOnError = false, RetryUncertain = false }.Validate("run");
        new BulkCommand { Where = "true", Operations = """[{"op":"set","path":"/v","value":1}]""", ETag = false }.Validate("patch");
    }

    [Theory]
    [InlineData("status", "dry-run")]
    [InlineData("status", "continue-on-error")]
    [InlineData("status", "etag")]
    [InlineData("status", "retry-uncertain")]
    [InlineData("begin", "yes")]
    public async Task UnsupportedFalseBooleanOptionsAreRejectedThroughShell(string subcommand, string option)
    {
        using var shell = ShellInterpreter.CreateInstance();

        var state = await shell.ExecuteCommandAsync($"bulk {subcommand} --{option}=false", TestContext.Current.CancellationToken);

        Assert.True(state.IsError);
        Assert.Contains($"--{option}", Assert.IsType<ErrorCommandState>(state).Exception.Message);
        Assert.Contains($"bulk {subcommand}", Assert.IsType<ErrorCommandState>(state).Exception.Message);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("add")]
    [InlineData("status")]
    [InlineData("show")]
    [InlineData("cancel")]
    public void ConcurrencyIsRejectedForNonExecutionSubcommands(string subcommand)
    {
        var command = new BulkCommand { Concurrency = BulkCommand.DefaultConcurrency };
        var error = Assert.Throws<CommandException>(() => command.Validate(subcommand));
        Assert.Contains("concurrency", error.Message);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("patch")]
    [InlineData("delete")]
    [InlineData("execute")]
    public void ConcurrencyIsAcceptedForExecutionSubcommands(string subcommand)
    {
        new BulkCommand
        {
            Concurrency = 5,
            Where = subcommand is "patch" or "delete" ? "true" : null,
            Operations = subcommand == "patch" ? """[{"op":"remove","path":"/old"}]""" : null,
        }.Validate(subcommand);
    }

    [Theory]
    [InlineData("concurrency")]
    [InlineData("max-parallelism")]
    public async Task ParserRejectsConcurrencyAliasesOnStatus(string option)
    {
        using var fixture = new Fixture();
        var error = await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk status --{option} 16"));
        Assert.Contains("concurrency", error.Message);
    }

    [Fact]
    public void SelectionQueryProjectsEscapedPartitionKeyPaths()
    {
        var query = BulkCommand.BuildSelectionQuery("c.expired = true", ["/tenant/id", "/re\"gion"]);
        Assert.Equal("""SELECT c.id, c._etag, c["tenant"]["id"] AS __pk0, c["re\"gion"] AS __pk1 FROM c WHERE (c.expired = true)""", query);
    }

    [Fact]
    public void SelectionQueryDecodesQuotedSegmentsAndEmbeddedSlashes()
    {
        var query = BulkCommand.BuildSelectionQuery("true", ["/\"tenant id\"", "/\"a/b\"/c"]);

        Assert.Equal("""SELECT c.id, c._etag, c["tenant id"] AS __pk0, c["a/b"]["c"] AS __pk1 FROM c WHERE (true)""", query);
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
            "upsert" => """{"op":"upsert","item":{"id":"1","pk":"a"}}""",
            "replace" => """{"op":"replace","item":{"id":"1","pk":"a"},"ifMatch":"tag"}""",
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
        Assert.Equal(op is "create" or "upsert" ? null : "tag", options.IfMatchEtag);
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

    [Theory]
    [InlineData(false, BulkOutcome.Failed)]
    [InlineData(true, BulkOutcome.Failed)]
    [InlineData(false, BulkOutcome.Uncertain)]
    [InlineData(true, BulkOutcome.Uncertain)]
    public async Task FailedWriteBlocksLaterWritesToSameItemUntilRetry(bool retryUncertain, string failureStatus)
    {
        var operations = new[]
        {
            new BulkOperation(0, "patch", "same", "[1]", """{"value":1}"""),
            new BulkOperation(1, "patch", "same", "[1.0]", """{"value":2}"""),
            new BulkOperation(2, "patch", "other", "[1]", """{"value":3}"""),
        };
        async IAsyncEnumerable<BulkOperation> OperationsAsync()
        {
            foreach (var operation in operations)
            {
                yield return operation;
            }

            await Task.CompletedTask;
        }

        var outcomes = new Dictionary<long, BulkOutcome>();
        var attempted = new List<long>();
        var fail = true;
        var value = 0;
        Task<BulkOutcome> WriteAsync(BulkOperation operation, CancellationToken token)
        {
            attempted.Add(operation.Index);
            if (operation.Index == 0 && fail)
            {
                return Task.FromResult(BulkOutcome.Create(operation, failureStatus, statusCode: failureStatus == BulkOutcome.Failed ? 429 : 503));
            }

            if (operation.Id == "same")
            {
                value = Json(operation.Json).GetProperty("value").GetInt32();
            }

            return Task.FromResult(BulkOutcome.Create(operation, BulkOutcome.Succeeded));
        }

        Task RecordAsync(BulkOutcome outcome)
        {
            outcomes[outcome.Index] = outcome;
            return Task.CompletedTask;
        }

        var first = new BulkSummary();
        await BulkExecutor.ExecuteAsync(OperationsAsync(), WriteAsync, RecordAsync, new Dictionary<long, BulkOutcome>(), first, 3, null, true, retryUncertain, TestContext.Current.CancellationToken);
        Assert.Equal([0L, 2L], attempted);
        Assert.Equal(0, value);
        Assert.Equal(1, first.Failed);
        Assert.Equal(1, first.Skipped);
        Assert.True(first.ResultIncomplete);

        fail = false;
        attempted.Clear();
        var retry = new BulkSummary();
        await BulkExecutor.ExecuteAsync(OperationsAsync(), WriteAsync, RecordAsync, new Dictionary<long, BulkOutcome>(outcomes), retry, 3, null, true, true, TestContext.Current.CancellationToken);
        Assert.Equal([0L, 1L], attempted);
        Assert.Equal(2, value);
        Assert.True(retry.Success);
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

    [Theory]
    [InlineData(BulkOutcome.Succeeded)]
    [InlineData(BulkOutcome.Started)]
    [InlineData(BulkOutcome.Uncertain)]
    public async Task ExhaustedRuBudgetDoesNotPreventLocalJournalSkips(string status)
    {
        var previous = Enumerable.Range(0, 3).ToDictionary(index => (long)index, index => Succeeded(index) with { Status = status });
        var summary = new BulkSummary { RequestCharge = 2 };
        var writes = new List<long>();
        var records = new List<BulkOutcome>();

        await BulkExecutor.ExecuteAsync(
            SourceAsync(3, TestContext.Current.CancellationToken),
            (operation, _) =>
            {
                writes.Add(operation.Index);
                return Task.FromResult(Succeeded(operation.Index));
            },
            outcome =>
            {
                records.Add(outcome);
                return Task.CompletedTask;
            },
            previous, summary, 1, 1, true, false, TestContext.Current.CancellationToken);

        Assert.Empty(writes);
        Assert.Empty(records);
        Assert.Equal(3, summary.Skipped);
        Assert.Equal(3, summary.Processed);
        Assert.Equal(0, summary.Attempted);
        Assert.Equal(2, summary.RequestCharge);
        Assert.False(summary.BudgetExceeded);
        Assert.False(summary.ResultIncomplete);
        Assert.Equal(status == BulkOutcome.Succeeded, summary.Success);
        Assert.Equal(status == BulkOutcome.Succeeded ? 0 : 3, summary.Uncertain);
    }

    [Fact]
    public async Task ExhaustedRuBudgetStillBlocksNewWritesAfterJournalSkips()
    {
        var previous = new Dictionary<long, BulkOutcome>
        {
            [0] = Succeeded(0),
            [1] = Succeeded(1) with { Status = BulkOutcome.Uncertain },
        };
        var summary = new BulkSummary { RequestCharge = 2 };
        var writes = new List<long>();

        await BulkExecutor.ExecuteAsync(
            SourceAsync(3, TestContext.Current.CancellationToken),
            (operation, _) =>
            {
                writes.Add(operation.Index);
                return Task.FromResult(Succeeded(operation.Index));
            },
            IgnoreAsync, previous, summary, 1, 1, true, false, TestContext.Current.CancellationToken);

        Assert.Empty(writes);
        Assert.Equal(2, summary.Skipped);
        Assert.Equal(1, summary.Uncertain);
        Assert.Equal(0, summary.Attempted);
        Assert.True(summary.BudgetExceeded);
        Assert.True(summary.ResultIncomplete);
    }

    [Theory]
    [InlineData(BulkOutcome.Failed, false)]
    [InlineData(BulkOutcome.Started, true)]
    [InlineData(BulkOutcome.Uncertain, true)]
    public async Task ExhaustedRuBudgetStillBlocksJournalRetries(string status, bool retryUncertain)
    {
        var previous = new Dictionary<long, BulkOutcome> { [0] = Succeeded(0) with { Status = status } };
        var summary = new BulkSummary { RequestCharge = 2 };
        var writes = new List<long>();
        var records = new List<BulkOutcome>();

        await BulkExecutor.ExecuteAsync(
            SourceAsync(1, TestContext.Current.CancellationToken),
            (operation, _) =>
            {
                writes.Add(operation.Index);
                return Task.FromResult(Succeeded(operation.Index));
            },
            outcome =>
            {
                records.Add(outcome);
                return Task.CompletedTask;
            },
            previous, summary, 1, 1, true, retryUncertain, TestContext.Current.CancellationToken);

        Assert.Empty(writes);
        Assert.Empty(records);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Attempted);
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
    public async Task JournalFailureDuringSettlementStillDrainsEveryStartedWrite()
    {
        using var timeout = Timeout();
        var first = new TaskCompletionSource<BulkOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<BulkOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var summary = new BulkSummary();
        var settled = new List<long>();
        var expected = new UnauthorizedAccessException("journal denied");
        var run = BulkExecutor.ExecuteAsync(
            SourceAsync(2, timeout.Token),
            (operation, _) => operation.Index == 0 ? first.Task : second.Task,
            outcome =>
            {
                if (outcome.Status != BulkOutcome.Started)
                {
                    settled.Add(outcome.Index);
                    throw expected;
                }

                return Task.CompletedTask;
            },
            new Dictionary<long, BulkOutcome>(),
            summary,
            2,
            null,
            true,
            false,
            timeout.Token);

        first.SetResult(Succeeded(0));
        Assert.False(run.IsCompleted);
        second.SetResult(Succeeded(1));

        var actual = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await run.WaitAsync(timeout.Token));
        Assert.Same(expected, actual);
        Assert.Equal([0L, 1L], settled);
        Assert.Equal(2, summary.Succeeded);
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

    [Theory]
    [InlineData(BulkOutcome.Started)]
    [InlineData(BulkOutcome.Uncertain)]
    public async Task ResumeHoldsBackDependentWritesButAllowsOtherItems(string status)
    {
        var operations = new[]
        {
            new BulkOperation(0, "patch", "same", "[1]", "{}"),
            new BulkOperation(1, "patch", "same", "[1.0]", "{}"),
            new BulkOperation(2, "patch", "same", "[2]", "{}"),
            new BulkOperation(3, "patch", "other", "[1]", "{}"),
        };
        async IAsyncEnumerable<BulkOperation> OperationsAsync()
        {
            foreach (var operation in operations)
            {
                yield return operation;
            }

            await Task.CompletedTask;
        }

        var previous = new Dictionary<long, BulkOutcome>
        {
            [0] = BulkOutcome.Create(operations[0], status),
        };
        var attempted = new List<long>();
        var records = new List<BulkOutcome>();
        var summary = new BulkSummary();
        await BulkExecutor.ExecuteAsync(
            OperationsAsync(),
            (operation, _) =>
            {
                attempted.Add(operation.Index);
                return Task.FromResult(BulkOutcome.Create(operation, BulkOutcome.Succeeded));
            },
            outcome =>
            {
                records.Add(outcome);
                return Task.CompletedTask;
            },
            previous, summary, 1, null, true, false, TestContext.Current.CancellationToken);

        Assert.Equal([2L, 3L], attempted);
        Assert.Equal(2, summary.Skipped);
        Assert.Equal(1, summary.Uncertain);
        Assert.True(summary.ResultIncomplete);
        Assert.DoesNotContain(records, outcome => outcome.Index is 0 or 1);

        attempted.Clear();
        await BulkExecutor.ExecuteAsync(
            OperationsAsync(),
            (operation, _) =>
            {
                attempted.Add(operation.Index);
                return Task.FromResult(BulkOutcome.Create(operation, BulkOutcome.Succeeded));
            },
            IgnoreAsync, previous, new BulkSummary(), 1, null, true, true, TestContext.Current.CancellationToken);
        Assert.Equal([0L, 1L, 2L, 3L], attempted);
    }

    [Fact]
    public async Task NewUncertainOutcomeAlsoHoldsBackDependentWrites()
    {
        var summary = new BulkSummary();
        var attempted = new List<long>();
        async IAsyncEnumerable<BulkOperation> OperationsAsync()
        {
            yield return new BulkOperation(0, "patch", "same", "[1]", "{}");
            yield return new BulkOperation(1, "patch", "same", "[1.0]", "{}");
            await Task.CompletedTask;
        }

        await BulkExecutor.ExecuteAsync(
            OperationsAsync(),
            (operation, _) =>
            {
                attempted.Add(operation.Index);
                return Task.FromResult(BulkOutcome.Create(operation, BulkOutcome.Uncertain));
            },
            IgnoreAsync, new Dictionary<long, BulkOutcome>(), summary, 1, null, true, false, TestContext.Current.CancellationToken);

        Assert.Equal([0L], attempted);
        Assert.Equal(1, summary.Skipped);
        Assert.True(summary.ResultIncomplete);
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

    [Theory]
    [InlineData("unrelated")]
    [InlineData("unrelated\npartial")]
    [InlineData("{\"op\":\"delete\",\"id\":\"0\",\"partitionKey\":\"a\"}")]
    [InlineData("{\"op\":\"delete\",\"id\":\"0\",\"partitionKey\":\"a\"}\n")]
    public async Task JournalRejectsUnrelatedFilesWithoutModifyingThem(string content)
    {
        using var fixture = new Fixture();
        var path = fixture.Directory.PathFor("operations.jsonl");
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk run '{{\"op\":\"delete\",\"id\":\"0\",\"partitionKey\":\"a\"}}' --journal '{path}' --yes --db db --con items"));
        Assert.Equal(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Writes());
    }

    [Fact]
    public async Task JournalCannotOverwriteTheOperationFile()
    {
        using var fixture = new Fixture();
        var path = fixture.Directory.PathFor("operations.jsonl");
        const string content = """{"op":"delete","id":"0","partitionKey":"a"}""";
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk run '{path}' --journal '{path}' --yes --db db --con items"));
        Assert.Equal(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Writes());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalRejectsMismatchedOrOlderHeadersWithoutTrimmingTail(bool oldVersion)
    {
        using var directory = new TempDirectory();
        var path = directory.PathFor("journal.jsonl");
        using (var journal = BulkJournal.Open(path, "endpoint", "rid", "hash", 2))
        {
            await journal.RecordAsync(Succeeded(0));
        }

        var content = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        if (oldVersion)
        {
            content = content.Replace("\"version\":2", "\"version\":1", StringComparison.Ordinal);
        }

        content += "{\"outcome\":";
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        Assert.Throws<CommandException>(() => BulkJournal.Open(path, "endpoint", "rid", oldVersion ? "hash" : "different", 2));
        Assert.Equal(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("index", "1")]
    [InlineData("op", "\"upsert\"")]
    [InlineData("id", "\"other\"")]
    [InlineData("partitionKey", "[\"other\"]")]
    [InlineData("status", "\"failed\"")]
    public async Task JournalRejectsChecksummedRecordCorruptionBeforeAnyWrites(string field, string value)
    {
        using var fixture = new Fixture();
        var path = fixture.Directory.PathFor("journal.jsonl");
        const string operations = """[{"op":"delete","id":"0","partitionKey":"a"},{"op":"delete","id":"1","partitionKey":"a"}]""";
        await fixture.RunAsync($"bulk run '{operations}' --journal '{path}' --yes --db db --con items");
        Assert.Equal(2, fixture.Writes().Count());
        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        var entry = JsonNode.Parse(lines[1])!;
        entry["outcome"]![field] = JsonNode.Parse(value);
        lines[1] = entry.ToJsonString();
        var content = string.Join("\n", lines) + "\n{\"outcome\":";
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk run '{operations}' --journal '{path}' --yes --db db --con items"));
        Assert.Equal(2, fixture.Writes().Count());
        Assert.Equal(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("op")]
    [InlineData("partitionKey")]
    public async Task JournalValidatesRecordIdentityAgainstCurrentOperationsBeforeAnyWrites(string field)
    {
        using var fixture = new Fixture();
        var path = fixture.Directory.PathFor("journal.jsonl");
        var outcome = Succeeded(1);
        outcome = field switch
        {
            "id" => outcome with { Id = "different" },
            "op" => outcome with { Op = "upsert" },
            _ => outcome with { PartitionKey = Json("""["other"]""") },
        };
        using (var journal = BulkJournal.Open(path, "https://unit-test.documents.azure.com/", "rid", BulkSpool.ComputeHash([Operation(0).Json, Operation(1).Json]), 2))
        {
            await journal.RecordAsync(outcome);
        }

        var error = await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk run '[{Operation(0).Json},{Operation(1).Json}]' --journal '{path}' --yes --db db --con items"));
        Assert.Contains("journal is damaged", error.Message);
        Assert.Empty(fixture.Writes());
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExecutionFailuresKeepPartialResultsAndOriginalClassification(bool cancel, bool throughShell)
    {
        using var fixture = new Fixture();
        using var cancellation = Timeout();
        Exception expected = cancel
            ? new OperationCanceledException(cancellation.Token)
            : new HttpRequestException("write transport failed");
        fixture.Container.DeleteItemAsync<JsonElement>("1", Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (cancel)
                {
                    await cancellation.CancelAsync();
                }

                return await Task.FromException<ItemResponse<JsonElement>>(expected);
            });

        var command = new BulkCommand
        {
            Subcommand = "run",
            Data = """[{"op":"delete","id":"0","partitionKey":"a"},{"op":"delete","id":"1","partitionKey":"a"},{"op":"delete","id":"2","partitionKey":"a"}]""",
            Database = "db",
            Container = "items",
            Yes = true,
            Concurrency = 1,
        };
        var state = throughShell
            ? await fixture.Shell.ExecuteCommandAsync(
                $"bulk run '{command.Data}' --db db --con items --concurrency 1 --yes",
                cancellation.Token)
            : await command.ExecuteAsync(fixture.Shell, new CommandState(), string.Empty, cancellation.Token);
        var error = Assert.IsType<StructuredErrorCommandState>(state);
        if (cancel)
        {
            var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error.Exception.GetBaseException());
            Assert.True(canceled.CancellationToken.IsCancellationRequested);
        }
        else
        {
            Assert.Same(expected, error.Exception.GetBaseException());
        }

        Assert.Equal(ShellExitCode.FromException(expected), error.ExitCode);
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.True(state.IsError);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.True(result.GetProperty("resultIncomplete").GetBoolean());
        Assert.Equal(1, result.GetProperty("succeeded").GetInt64());
        Assert.Equal(1, result.GetProperty("uncertain").GetInt64());
        Assert.Equal(2, result.GetProperty("attempted").GetInt64());
        Assert.Equal(7, state.RequestCharge);
        var mcp = McpResponseFactory.CreateSuccess(state, fixture.Shell.State);
        Assert.True(mcp.IsError);
        var payload = JsonSerializer.SerializeToElement(mcp.StructuredContent);
        Assert.Equal(1, payload.GetProperty("result").GetProperty("succeeded").GetInt64());
        Assert.Equal(1, payload.GetProperty("result").GetProperty("uncertain").GetInt64());
        Assert.Equal(7, payload.GetProperty("requestCharge").GetDouble());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunRejectsConditionalUpsertsBeforeAnyWrites(bool dryRun)
    {
        using var fixture = new Fixture();
        var option = dryRun ? "--dry-run" : "--yes";
        var error = await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync(
            $$"""bulk run '[{"op":"delete","id":"1","partitionKey":"a"},{"op":"upsert","item":{"id":"2","pk":"a"},"ifMatch":"tag"}]' {{option}} --db db --con items"""));
        Assert.Contains("Use replace for conditional item updates", error.Message);
        Assert.Empty(fixture.Writes());
    }

    [Fact]
    public async Task InvalidJournalRemainsAPreExecutionValidationError()
    {
        using var fixture = new Fixture();
        var path = fixture.Directory.PathFor("invalid.jsonl");
        await File.WriteAllTextAsync(path, "not a journal\n", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync(
            $"bulk run '{Operation(0).Json}' --journal '{path}' --yes --db db --con items"));

        Assert.Empty(fixture.Writes());
        Assert.Equal("not a journal\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StatefulAddRejectsConditionalUpsertsAtomically()
    {
        using var fixture = new Fixture();
        await fixture.RunAsync("bulk begin --partition-key a --db db --con items");
        await fixture.RunAsync("""bulk add '{"op":"delete","id":"1"}'""");
        var bulk = fixture.Shell.CurrentBulk!;
        var bytes = bulk.CanonicalJsonBytes;
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync(
            """bulk add '[{"op":"delete","id":"2"},{"op":"upsert","item":{"id":"3","pk":"a"},"ifMatch":"tag"}]'"""));
        Assert.Single(bulk.Operations);
        Assert.Equal(bytes, bulk.CanonicalJsonBytes);
        Assert.Empty(fixture.Writes());
    }

    [Theory]
    [InlineData(404)]
    [InlineData(412)]
    public async Task ConditionalReplaceDoesNotCreateMissingOrChangedItems(int status)
    {
        using var fixture = new Fixture();
        fixture.Container.ReplaceItemAsync(default(JsonElement), default!, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs<Task<ItemResponse<JsonElement>>>(_ => throw new CosmosException("failure", (HttpStatusCode)status, 0, "activity", 1));
        var state = await fixture.RunAsync(
            """bulk run '{"op":"replace","item":{"id":"1","pk":"a"},"ifMatch":"tag"}' --yes --db db --con items""");
        Assert.True(state.IsError);
        var write = Assert.Single(fixture.Writes());
        Assert.Equal("ReplaceItemAsync", write.GetMethodInfo().Name);
        Assert.Equal("tag", Assert.Single(write.GetArguments().OfType<ItemRequestOptions>()).IfMatchEtag);
        Assert.Equal(status, Assert.IsType<ShellJson>(state.Result).Value.GetProperty("errors")[0].GetProperty("statusCode").GetInt32());
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
    [InlineData("run", "[\"tenant\"")]
    [InlineData("run", "{\"pk\":")]
    [InlineData("run", "\"tenant")]
    [InlineData("begin", "[\"tenant\"")]
    [InlineData("begin", "{\"pk\":")]
    [InlineData("begin", "\"tenant")]
    public async Task MalformedDefaultPartitionKeyDoesNotWriteOrStartAJob(string subcommand, string raw)
    {
        using var fixture = new Fixture();
        var data = subcommand == "run" ? """ '{"op":"delete","id":"1"}' --yes""" : string.Empty;

        var error = await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync(
            $"bulk {subcommand}{data} --partition-key '{raw}' --db db --con items"));

        Assert.IsType<JsonException>(error.InnerException);
        Assert.Empty(fixture.Writes());
        Assert.Null(fixture.Shell.CurrentBulk);
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

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(1L, 1)]
    [InlineData(999L, 999)]
    [InlineData(1000L, 1000)]
    [InlineData(1001L, 1000)]
    [InlineData(long.MaxValue, 1000)]
    public async Task WhereSelectionBoundsQueryPageSizeByMaximumItems(long? maxItems, int expectedPageSize)
    {
        using var fixture = new Fixture();
        fixture.Query("""{"id":"a","__pk0":"t"}""", """{"id":"b","__pk0":"t"}""");
        var command = new BulkCommand
        {
            Subcommand = "delete",
            Where = "true",
            DryRun = true,
            MaxItems = maxItems,
            Database = "db",
            Container = "items",
        };
        var state = await command.ExecuteAsync(fixture.Shell, new CommandState(), string.Empty, TestContext.Current.CancellationToken);
        Assert.False(state.IsError);
        Assert.Equal(expectedPageSize, fixture.LastQueryPageSize);
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal(maxItems == 1 ? 1L : 2L, result.GetProperty("operationCount").GetInt64());
        Assert.Equal(maxItems == 1, result.GetProperty("selectionLimited").GetBoolean());
        Assert.Empty(fixture.Writes());
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
    public async Task StatefulStatusRendersTheSameOutcomeCountsAsJson()
    {
        using var fixture = new Fixture();
        await fixture.RunAsync("bulk begin --partition-key a --db db --con items");
        await fixture.RunAsync($"bulk add '[{string.Join(",", Enumerable.Range(0, 5).Select(index => Operation(index).Json))}]'");
        var bulk = fixture.Shell.CurrentBulk!;
        bulk.Outcomes[0] = Succeeded(0);
        bulk.Outcomes[1] = Succeeded(1);
        bulk.Outcomes[2] = Succeeded(2) with { Status = BulkOutcome.Failed };
        bulk.Outcomes[3] = Succeeded(3) with { Status = BulkOutcome.Uncertain };
        bulk.Outcomes[4] = Succeeded(4) with { Status = BulkOutcome.Started };
        var state = await fixture.RunAsync("bulk status");
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal(2, result.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Equal(2, result.GetProperty("uncertain").GetInt32());

        var saved = AnsiConsole.Console;
        using var output = new StringWriter();
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            });
            state.RenderUser!();
        }
        finally
        {
            AnsiConsole.Console = saved;
        }

        Assert.Matches(@"Succeeded[^\r\n]*\b2\b", output.ToString());
        Assert.Matches(@"Failed[^\r\n]*\b1\b", output.ToString());
        Assert.Matches(@"Uncertain[^\r\n]*\b2\b", output.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatefulRetriesCannotAddChangeOrOmitOriginalJournal(bool useJournal)
    {
        using var fixture = new Fixture();
        var original = fixture.Directory.PathFor("original.jsonl");
        var replacement = fixture.Directory.PathFor("replacement.jsonl");
        await fixture.RunAsync("bulk begin --partition-key a --db db --con items");
        await fixture.RunAsync("""bulk add '[{"op":"patch","id":"1","operations":[{"op":"incr","path":"/n","value":1}]},{"op":"patch","id":"2","operations":[{"op":"incr","path":"/n","value":1}]}]'""");
        fixture.FailPatchFor("2", HttpStatusCode.PreconditionFailed);
        var journalOption = useJournal ? $"--journal '{original}'" : string.Empty;
        var first = await fixture.RunAsync($"bulk execute --yes {journalOption}");
        Assert.True(first.IsError);
        Assert.Equal(2, fixture.Writes().Count());
        var bulk = fixture.Shell.CurrentBulk!;
        var bytes = bulk.CanonicalJsonBytes;
        if (useJournal)
        {
            var originalContent = await File.ReadAllTextAsync(original, TestContext.Current.CancellationToken);
            var add = await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("""bulk add '{"op":"delete","id":"3"}'"""));
            Assert.Contains("Cannot add operations", add.Message);
            Assert.Equal(2, bulk.Operations.Count);
            Assert.Equal(bytes, bulk.CanonicalJsonBytes);
            Assert.Equal(originalContent, await File.ReadAllTextAsync(original, TestContext.Current.CancellationToken));
        }

        var addedOrChanged = await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk execute --yes --journal '{replacement}'"));
        Assert.Contains("same journal choice", addedOrChanged.Message);
        Assert.False(File.Exists(replacement));
        if (useJournal)
        {
            var omitted = await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("bulk execute --yes"));
            Assert.Contains("same journal choice", omitted.Message);
        }

        Assert.Equal(2, fixture.Writes().Count());
        fixture.FailPatchFor(null, HttpStatusCode.OK);
        var retry = await fixture.RunAsync($"bulk execute --yes {journalOption}");
        Assert.False(retry.IsError);
        Assert.Equal(1, Assert.IsType<ShellJson>(retry.Result).Value.GetProperty("skipped").GetInt64());
        Assert.Equal(3, fixture.Writes().Count());
        if (useJournal)
        {
            var operations = new[]
            {
                BulkOperationNormalizer.Normalize(Json("""{"op":"patch","id":"1","operations":[{"op":"incr","path":"/n","value":1}]}"""), SinglePath, """["a"]""", 0),
                BulkOperationNormalizer.Normalize(Json("""{"op":"patch","id":"2","operations":[{"op":"incr","path":"/n","value":1}]}"""), SinglePath, """["a"]""", 1),
            };
            using var reopened = BulkJournal.Open(original, "https://unit-test.documents.azure.com/", "rid", BulkSpool.ComputeHash(operations.Select(operation => operation.Json)), 2);
            Assert.Equal(BulkOutcome.Succeeded, reopened.Previous[0].Status);
            Assert.Equal(BulkOutcome.Succeeded, reopened.Previous[1].Status);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task StatefulRetryDoesNotOverwriteAnotherShellsNewerJournalOutcome(HttpStatusCode status)
    {
        using var first = new Fixture();
        using var second = new Fixture();
        var path = first.Directory.PathFor("shared.jsonl");
        const string operation = """{"op":"patch","id":"1","partitionKey":"a","operations":[{"op":"incr","path":"/n","value":1}]}""";
        await first.RunAsync("bulk begin --db db --con items");
        await first.RunAsync($"bulk add '{operation}'");
        first.FailPatchFor("1", HttpStatusCode.PreconditionFailed);
        Assert.True((await first.RunAsync($"bulk execute --journal '{path}' --yes")).IsError);
        Assert.Equal(BulkOutcome.Failed, first.Shell.CurrentBulk!.Outcomes[0].Status);

        if (status != HttpStatusCode.OK)
        {
            second.FailPatchFor("1", status);
        }

        await second.RunAsync($"bulk run '{operation}' --journal '{path}' --yes --db db --con items");
        first.FailPatchFor(null, HttpStatusCode.OK);
        var retry = await first.RunAsync($"bulk execute --journal '{path}' --yes");

        Assert.Single(first.Writes());
        Assert.Single(second.Writes());
        var result = Assert.IsType<ShellJson>(retry.Result).Value;
        Assert.Equal(0, result.GetProperty("attempted").GetInt64());
        Assert.Equal(1, result.GetProperty("skipped").GetInt64());
        Assert.Equal(status != HttpStatusCode.OK, retry.IsError);
        Assert.Equal(status == HttpStatusCode.OK ? 0 : 1, result.GetProperty("uncertain").GetInt64());
    }

    [Fact]
    public async Task StatefulRetryRetainsSuccessMissingFromDurableJournal()
    {
        using var fixture = new Fixture();
        var path = fixture.Directory.PathFor("journal.jsonl");
        await fixture.RunAsync("bulk begin --db db --con items");
        await fixture.RunAsync($"bulk add '{Operation(0).Json}'");
        var bulk = fixture.Shell.CurrentBulk!;
        bulk.JournalPath = path;
        bulk.Outcomes[0] = Succeeded(0);
        using (var journal = BulkJournal.Open(path, "https://unit-test.documents.azure.com/", "rid", BulkSpool.ComputeHash([Operation(0).Json]), 1))
        {
            await journal.RecordAsync(Succeeded(0) with { Status = BulkOutcome.Started });
        }

        var result = await fixture.RunAsync($"bulk execute --journal '{path}' --yes");

        Assert.False(result.IsError);
        Assert.Empty(fixture.Writes());
        Assert.Equal(1, Assert.IsType<ShellJson>(result.Result).Value.GetProperty("skipped").GetInt64());
    }

    [Theory]
    [InlineData(10_000, 64L * 1024 * 1024)]
    [InlineData(0, 0)]
    public void StatefulCapacityAcceptsExactLimits(int count, long bytes)
    {
        PendingBulkState.ValidateCapacity(count, bytes);
    }

    [Fact]
    public async Task StatefulJobResumingOnlyUncertainOutcomesCannotBeExtended()
    {
        using var fixture = new Fixture();
        var path = fixture.Directory.PathFor("journal.jsonl");
        using (var journal = BulkJournal.Open(path, "https://unit-test.documents.azure.com/", "rid", BulkSpool.ComputeHash([Operation(0).Json]), 1))
        {
            await journal.RecordAsync(Succeeded(0) with { Status = BulkOutcome.Started });
        }

        await fixture.RunAsync("bulk begin --db db --con items");
        await fixture.RunAsync($"bulk add '{Operation(0).Json}'");
        var result = await fixture.RunAsync($"bulk execute --journal '{path}' --yes");
        Assert.True(result.IsError);
        Assert.Equal(BulkOutcome.Started, fixture.Shell.CurrentBulk!.Outcomes[0].Status);
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync($"bulk add '{Operation(1).Json}'"));
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("bulk execute --yes"));
        Assert.Single(fixture.Shell.CurrentBulk.Operations);
        Assert.Empty(fixture.Writes());
    }

    [Theory]
    [InlineData(10_001, 0)]
    [InlineData(1, (64L * 1024 * 1024) + 1)]
    public void StatefulCapacityRejectsExceededLimits(int count, long bytes)
    {
        var error = Assert.Throws<CommandException>(() => PendingBulkState.ValidateCapacity(count, bytes));
        Assert.Contains("10,000 operations and 64 MiB", error.Message);
    }

    [Fact]
    public async Task StatefulAddRejectsOverLimitFileAtomically()
    {
        using var fixture = new Fixture();
        await fixture.RunAsync("bulk begin --partition-key a --db db --con items");
        var path = fixture.Directory.PathFor("operations.jsonl");
        await File.WriteAllLinesAsync(
            path,
            Enumerable.Range(0, PendingBulkState.MaxOperations - 1).Select(index => $$"""{"op":"delete","id":"{{index}}"}"""),
            TestContext.Current.CancellationToken);
        await fixture.RunAsync($"bulk add '{path}'");
        var bulk = fixture.Shell.CurrentBulk!;
        Assert.Equal(PendingBulkState.MaxOperations - 1, bulk.Operations.Count);
        var bytes = bulk.CanonicalJsonBytes;

        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("""bulk add '[{"op":"delete","id":"one"},{"op":"delete","id":"two"}]'"""));
        Assert.Equal(PendingBulkState.MaxOperations - 1, bulk.Operations.Count);
        Assert.Equal(bytes, bulk.CanonicalJsonBytes);
        await fixture.RunAsync("""bulk add '{"op":"delete","id":"last"}'""");
        Assert.Equal(PendingBulkState.MaxOperations, bulk.Operations.Count);
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("""bulk add '{"op":"delete","id":"extra"}'"""));
        Assert.Equal(PendingBulkState.MaxOperations, bulk.Operations.Count);
        Assert.Empty(fixture.Writes());
    }

    [Fact]
    public async Task StatefulByteLimitUsesCanonicalUtf8AndRejectsAddAtomically()
    {
        using var fixture = new Fixture();
        await fixture.RunAsync("bulk begin --partition-key a --db db --con items");
        await fixture.RunAsync("""bulk add '{"op":"upsert","item":{"id":"1","pk":"a","name":"\u00e9"}}'""");
        var bulk = fixture.Shell.CurrentBulk!;
        var operation = Assert.Single(bulk.Operations);
        Assert.Equal(Encoding.UTF8.GetByteCount(operation.Json), bulk.CanonicalJsonBytes);
        Assert.True(bulk.CanonicalJsonBytes > operation.Json.Length);

        bulk.CanonicalJsonBytes = PendingBulkState.MaxCanonicalJsonBytes;
        await Assert.ThrowsAsync<CommandException>(() => fixture.RunAsync("""bulk add '{"op":"delete","id":"2"}'"""));
        Assert.Single(bulk.Operations);
        Assert.Equal(PendingBulkState.MaxCanonicalJsonBytes, bulk.CanonicalJsonBytes);
        Assert.Empty(fixture.Writes());
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
        Assert.Contains("saving a plan creates a server-local file and requires confirmation", tool.Description);
        Assert.Contains("Only dry runs without save skip confirmation", tool.Description);
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

        public string PathFor(string name) => Path.Join(this.directory.FullName, name);

        public void Dispose() => this.directory.Delete(recursive: true);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<ResponseMessage> metadataResponses = [];
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
                this.metadataResponses.Add(response);
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

        public int? LastQueryPageSize { get; private set; }

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
                this.LastQueryPageSize = call.ArgAt<QueryRequestOptions>(2).MaxItemCount;
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
            foreach (var response in this.metadataResponses)
            {
                response.Dispose();
            }

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
