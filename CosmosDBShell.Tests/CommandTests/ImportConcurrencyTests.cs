// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Azure.Cosmos;
using NSubstitute;

public class ImportConcurrencyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public async Task Writes_OverlapUpToLimitAndDrainBeforeReturning(int concurrency)
    {
        using var timeout = CreateTimeout();
        var container = Substitute.For<Container>();
        var gates = Enumerable.Range(0, concurrency + 2).Select(_ => NewGate()).ToArray();
        var started = Enumerable.Range(0, gates.Length).Select(_ => NewGate()).ToArray();
        var active = 0;
        var peak = 0;
        ConfigureWrites(container, async (item, token) =>
        {
            var index = item.GetProperty("id").GetInt32();
            var current = Interlocked.Increment(ref active);
            peak = Math.Max(peak, current);
            started[index].TrySetResult();
            try
            {
                await gates[index].Task.WaitAsync(token);
                return Response(HttpStatusCode.Created, 1.25);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        using var reader = NewReader(gates.Length);
        var import = ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, timeout.Token),
            container, ImportMode.Insert, concurrency, false, timeout.Token);
        try
        {
            await Task.WhenAll(started.Take(concurrency).Select(gate => gate.Task)).WaitAsync(timeout.Token);
            Assert.Equal(concurrency, active);
            Assert.False(started[concurrency].Task.IsCompleted);
            Assert.False(import.IsCompleted);

            // Completing the last write must free a slot even while earlier writes are blocked.
            gates[concurrency - 1].TrySetResult();
            await started[concurrency].Task.WaitAsync(timeout.Token);
            Assert.Equal(concurrency, active);
            Assert.False(started[concurrency + 1].Task.IsCompleted);
        }
        finally
        {
            foreach (var gate in gates)
            {
                gate.TrySetResult();
            }
        }

        var result = await import.WaitAsync(timeout.Token);
        Assert.Equal((gates.Length, 0, gates.Length * 1.25), result);
        Assert.Equal(concurrency, peak);
        Assert.Equal(0, active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteFailure_StopsOrContinuesAndCountsInflightResults(bool continueOnError)
    {
        using var timeout = CreateTimeout();
        var container = Substitute.For<Container>();
        var first = new TaskCompletionSource<ItemResponse<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ItemResponse<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new List<int>();
        ConfigureWrites(container, (item, _) =>
        {
            var id = item.GetProperty("id").GetInt32();
            started.Add(id);
            if (id == 0)
            {
                return first.Task;
            }

            if (id == 1)
            {
                return second.Task;
            }

            if (id == 2)
            {
                return Task.FromException<ItemResponse<JsonElement>>(
                    new CosmosException("conflict", HttpStatusCode.Conflict, 0, "test", 3));
            }

            return Task.FromResult(Response(HttpStatusCode.Created, 2));
        });
        using var reader = NewReader(5);
        var import = ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, timeout.Token),
            container, ImportMode.Insert, 3, continueOnError, timeout.Token);
        Assert.Equal(continueOnError ? 5 : 3, started.Count);
        Assert.False(import.IsCompleted);
        first.SetResult(Response(HttpStatusCode.Created, 2));
        second.SetResult(Response(HttpStatusCode.Created, 2));

        var result = await import.WaitAsync(timeout.Token);
        Assert.Equal(continueOnError ? (4, 1, 11.0) : (2, 1, 7.0), result);
        Assert.Equal(continueOnError ? 5 : 3, started.Count);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.Created, 1)]
    [InlineData(0, HttpStatusCode.OK, 0)]
    [InlineData(1, HttpStatusCode.Created, 1)]
    [InlineData(1, HttpStatusCode.OK, 1)]
    [InlineData(1, HttpStatusCode.BadRequest, 0)]
    public async Task WriteMode_UsesCorrectApiAndCountsResponseStatus(int mode, HttpStatusCode status, int expectedSuccess)
    {
        using var timeout = CreateTimeout();
        var container = Substitute.For<Container>();
        var calls = 0;
        ConfigureWrites(container, (_, _) =>
        {
            calls++;
            return Task.FromResult(Response(status, 2.5));
        }, (ImportMode)mode);
        using var reader = NewReader(1);
        var result = await ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, timeout.Token),
            container, (ImportMode)mode, 16, false, timeout.Token);

        Assert.Equal(1, calls);
        Assert.Equal((expectedSuccess, 1 - expectedSuccess, 2.5), result);
    }

    [Fact]
    public async Task SequentialWrites_PreserveFileOrderAndStopAtFailure()
    {
        using var timeout = CreateTimeout();
        var container = Substitute.For<Container>();
        var ids = new List<int>();
        ConfigureWrites(container, (item, _) =>
        {
            var id = item.GetProperty("id").GetInt32();
            ids.Add(id);
            return Task.FromResult(Response(id == 2 ? HttpStatusCode.BadRequest : HttpStatusCode.Created, 1));
        });
        using var reader = NewReader(10);
        var result = await ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, timeout.Token),
            container, ImportMode.Insert, 1, false, timeout.Token);

        Assert.Equal(new[] { 0, 1, 2 }, ids);
        Assert.Equal((2, 1, 3.0), result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseFailure_DrainsInflightWriteAndPreservesError(bool continueOnError)
    {
        using var timeout = CreateTimeout();
        var container = Substitute.For<Container>();
        var write = new TaskCompletionSource<ItemResponse<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        ConfigureWrites(container, (_, _) =>
        {
            calls++;
            return write.Task;
        });
        using var reader = new StringReader("{\"id\":0}\nnot-json\n{\"id\":2}\n");
        var import = ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, timeout.Token),
            container, ImportMode.Insert, 3, continueOnError, timeout.Token);
        Assert.Equal(1, calls);
        Assert.False(import.IsCompleted);
        write.SetResult(Response(HttpStatusCode.Created, 1));

        var error = await Assert.ThrowsAsync<CommandException>(() => import.WaitAsync(timeout.Token));
        Assert.Contains("2", error.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancellation_PropagatesToWritesAndDrainsThem()
    {
        using var timeout = CreateTimeout();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var container = Substitute.For<Container>();
        var bothStarted = NewGate();
        var active = 0;
        var started = 0;
        ConfigureWrites(container, async (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            Interlocked.Increment(ref active);
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Response(HttpStatusCode.Created, 1);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        using var reader = NewReader(10);
        var import = ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, cancellation.Token),
            container, ImportMode.Insert, 2, true, cancellation.Token);
        await bothStarted.Task.WaitAsync(timeout.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => import.WaitAsync(timeout.Token));
        Assert.Equal(2, started);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task UnexpectedWriteFailure_IsSurfacedAfterOtherWritesSettle()
    {
        using var timeout = CreateTimeout();
        var container = Substitute.For<Container>();
        var writes = Enumerable.Range(0, 2)
            .Select(_ => new TaskCompletionSource<ItemResponse<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        ConfigureWrites(container, (item, _) => writes[item.GetProperty("id").GetInt32()].Task);
        using var reader = NewReader(2);
        var import = ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, timeout.Token),
            container, ImportMode.Insert, 2, true, timeout.Token);
        var expected = new InvalidOperationException("unexpected write failure");
        writes[0].SetException(expected);
        Assert.False(import.IsCompleted);
        writes[1].SetResult(Response(HttpStatusCode.Created, 1));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => import.WaitAsync(timeout.Token));
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Cancellation_WhileDrainingDoesNotReturnSuccessWhenWriteIgnoresToken()
    {
        using var timeout = CreateTimeout();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var container = Substitute.For<Container>();
        var write = new TaskCompletionSource<ItemResponse<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureWrites(container, (_, _) => write.Task);
        using var reader = NewReader(1);
        var import = ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, cancellation.Token),
            container, ImportMode.Insert, 2, false, cancellation.Token);
        Assert.False(import.IsCompleted);
        await cancellation.CancelAsync();
        Assert.False(import.IsCompleted);
        write.SetResult(Response(HttpStatusCode.Created, 1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => import.WaitAsync(timeout.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidConcurrency_IsRejectedBeforeConnectionOrFileAccess(int concurrency)
    {
        var command = new ImportCommand { Concurrency = concurrency };
        var error = await Assert.ThrowsAsync<CommandException>(() =>
            command.ExecuteAsync(ShellInterpreter.Instance, new CommandState(), "import", TestContext.Current.CancellationToken));
        Assert.Contains("positive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptySource_DoesNotWrite()
    {
        using var reader = NewReader(0);
        var container = Substitute.For<Container>();
        var result = await ImportCommand.WriteItemsAsync(
            ImportCommand.EnumerateJsonLinesAsync(reader, TestContext.Current.CancellationToken),
            container, ImportMode.Insert, 16, false, TestContext.Current.CancellationToken);
        Assert.Equal((0, 0, 0.0), result);
        Assert.Empty(container.ReceivedCalls());
    }

    [Theory]
    [InlineData("jsonl", "{\"id\":\"a\"}\n{\"id\":\"b\"}\n")]
    [InlineData("json", "[{\"id\":\"a\"},{\"id\":\"b\"}]")]
    [InlineData("csv", "id\na\nb\n")]
    public async Task DryRun_WithConcurrencyValidatesWithoutConnectionAndPreservesResult(string extension, string content)
    {
        var fileName = $"cosmos-import-{Guid.NewGuid():N}.{Path.GetFileName(extension).TrimStart('.')}";
        var path = Path.Join(Path.GetTempPath(), fileName);
        try
        {
            await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
            var command = new ImportCommand { File = path, DryRun = true, Concurrency = 32 };
            var state = await command.ExecuteAsync(
                ShellInterpreter.Instance, new CommandState(), "import", TestContext.Current.CancellationToken);
            var result = Assert.IsType<ShellJson>(state.Result).Value;

            Assert.Equal("import", result.GetProperty("type").GetString());
            Assert.Equal(2, result.GetProperty("imported").GetInt32());
            Assert.Equal(0, result.GetProperty("failed").GetInt32());
            Assert.Equal(0, result.GetProperty("requestCharge").GetDouble());
            Assert.True(result.GetProperty("dryRun").GetBoolean());
            Assert.Null(state.RequestCharge);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CancellationTokenSource CreateTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return timeout;
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static StringReader NewReader(int count) =>
        new(string.Join('\n', Enumerable.Range(0, count).Select(id => JsonSerializer.Serialize(new { id }))));

    private static ItemResponse<JsonElement> Response(HttpStatusCode status, double charge)
    {
        var response = Substitute.For<ItemResponse<JsonElement>>();
        response.StatusCode.Returns(status);
        response.RequestCharge.Returns(charge);
        return response;
    }

    private static void ConfigureWrites(
        Container container,
        Func<JsonElement, CancellationToken, Task<ItemResponse<JsonElement>>> write,
        ImportMode mode = ImportMode.Insert)
    {
        Task<ItemResponse<JsonElement>> Invoke(NSubstitute.Core.CallInfo call)
        {
            Assert.False(call.ArgAt<ItemRequestOptions>(2).EnableContentResponseOnWrite);
            return write(call.ArgAt<JsonElement>(0), call.ArgAt<CancellationToken>(3));
        }

        if (mode == ImportMode.Upsert)
        {
            container.UpsertItemAsync(Arg.Any<JsonElement>(), Arg.Any<PartitionKey?>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(Invoke);
        }
        else
        {
            container.CreateItemAsync(Arg.Any<JsonElement>(), Arg.Any<PartitionKey?>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(Invoke);
        }
    }
}
