// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Unit tests for <see cref="BatchOperationParser"/>. These cover the pure parsing and
/// validation logic, which can be exercised without a live Cosmos DB connection.
/// </summary>
public class BatchCommandTests
{
    [Fact]
    public async Task Status_WithoutActiveBatch_ReturnsInactive()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new BatchCommand { Subcommand = "status" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "batch status", CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Json));
        Assert.False(json.GetProperty("active").GetBoolean());
        Assert.NotNull(state.RenderUser);
    }

    [Fact]
    public async Task Show_WithoutActiveBatch_ReturnsEmptyArray()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new BatchCommand { Subcommand = "show" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "batch show", CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Json));
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Empty(json.EnumerateArray());
    }

    [Fact]
    public async Task Status_WithActiveBatch_ReturnsBatchDetails()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.CurrentBatch = CreatePendingBatch();
        shell.CurrentBatch.Operations.AddRange(BatchOperationParser.Parse(
            "batch",
            "[{\"op\":\"create\",\"item\":{\"id\":\"1\"}},{\"op\":\"delete\",\"id\":\"2\"}]"));
        var command = new BatchCommand { Subcommand = "status" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "batch status", CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Json));
        Assert.True(json.GetProperty("active").GetBoolean());
        Assert.Equal("TestDatabase", json.GetProperty("database").GetString());
        Assert.Equal("TestContainer", json.GetProperty("container").GetString());
        Assert.Equal("tenant-1", json.GetProperty("partitionKey").GetString());
        Assert.Equal(2, json.GetProperty("operationCount").GetInt32());
        Assert.Equal("1", json.GetProperty("operations")[0].GetProperty("id").GetString());
        Assert.Equal("delete", json.GetProperty("operations")[1].GetProperty("op").GetString());
        Assert.NotNull(state.RenderUser);
    }

    [Fact]
    public async Task Show_WithActiveBatch_ReturnsOriginalOperations()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.CurrentBatch = CreatePendingBatch();
        shell.CurrentBatch.Operations.AddRange(BatchOperationParser.Parse(
            "batch",
            "[{\"op\":\"create\",\"item\":{\"id\":\"1\",\"name\":\"Ada\"}},{\"op\":\"delete\",\"id\":\"2\"}]"));
        var command = new BatchCommand { Subcommand = "show" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "batch show", CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Json));
        Assert.Equal(2, json.GetArrayLength());
        Assert.Equal("Ada", json[0].GetProperty("item").GetProperty("name").GetString());
        Assert.Equal("2", json[1].GetProperty("id").GetString());
        Assert.Null(state.RenderUser);
    }

    [Fact]
    public async Task Add_WithData_QueuesOperations()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.CurrentBatch = CreatePendingBatch();
        var command = new BatchCommand
        {
            Subcommand = "add",
            Data = "[{\"op\":\"create\",\"item\":{\"id\":\"1\"}},{\"op\":\"delete\",\"id\":\"2\"}]",
        };

        var state = await command.ExecuteAsync(shell, new CommandState(), "batch add", CancellationToken.None);

        Assert.Equal(2, shell.CurrentBatch.Operations.Count);
        Assert.NotNull(state.RenderUser);
    }

    [Fact]
    public async Task Add_WithPipedJson_QueuesOperation()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.CurrentBatch = CreatePendingBatch();
        var command = new BatchCommand { Subcommand = "add" };
        var input = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                op = "delete",
                id = "piped-item",
            })),
        };

        await command.ExecuteAsync(shell, input, "batch add", CancellationToken.None);

        var operation = Assert.Single(shell.CurrentBatch.Operations);
        Assert.Equal("piped-item", operation.Id);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("abort")]
    public async Task Cancel_WithActiveBatch_ClearsBatch(string subcommand)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.CurrentBatch = CreatePendingBatch();
        var command = new BatchCommand { Subcommand = subcommand };

        var state = await command.ExecuteAsync(shell, new CommandState(), $"batch {subcommand}", CancellationToken.None);

        Assert.Null(shell.CurrentBatch);
        Assert.NotNull(state.RenderUser);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("cancel")]
    public async Task StatefulCommand_WithoutActiveBatch_Throws(string subcommand)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new BatchCommand
        {
            Subcommand = subcommand,
            Data = "{\"op\":\"delete\",\"id\":\"1\"}",
        };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), $"batch {subcommand}", CancellationToken.None));

        Assert.Equal(MessageService.GetString("command-batch-error-not_active"), exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task InvalidSubcommand_Throws(string subcommand)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new BatchCommand { Subcommand = subcommand };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "batch", CancellationToken.None));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("begin")]
    [InlineData("execute")]
    [InlineData("exec")]
    [InlineData("commit")]
    public async Task ConnectedCommand_WhenDisconnected_Throws(string subcommand)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new BatchCommand { Subcommand = subcommand };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), $"batch {subcommand}", CancellationToken.None));
    }

    [Fact]
    public async Task Begin_WhenBatchAlreadyActive_Throws()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        shell.CurrentBatch = CreatePendingBatch();
        var command = new BatchCommand { Subcommand = "begin", PartitionKeyArgument = "tenant-1" };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "batch begin", CancellationToken.None));

        Assert.Equal(MessageService.GetString("command-batch-error-already_active"), exception.Message);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("begin")]
    public async Task StartCommand_WithoutPartitionKey_Throws(string subcommand)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new BatchCommand { Subcommand = subcommand };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), $"batch {subcommand}", CancellationToken.None));

        Assert.Equal(MessageService.GetString("command-batch-error-missing_pk"), exception.Message);
    }

    [Fact]
    public async Task Execute_WithEmptyActiveBatch_Throws()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        shell.CurrentBatch = CreatePendingBatch();
        var command = new BatchCommand { Subcommand = "execute" };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "batch execute", CancellationToken.None));

        Assert.Equal(MessageService.GetString("command-batch-error-empty"), exception.Message);
    }

    [Fact]
    public async Task Add_WhenBatchWouldExceedMaximum_Throws()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.CurrentBatch = CreatePendingBatch();
        var existingOperation = BatchOperationParser.Parse("batch", "{\"op\":\"delete\",\"id\":\"1\"}")[0];
        shell.CurrentBatch.Operations.AddRange(Enumerable.Repeat(existingOperation, BatchExecutor.MaxOperations));
        var command = new BatchCommand
        {
            Subcommand = "add",
            Data = "{\"op\":\"delete\",\"id\":\"101\"}",
        };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "batch add", CancellationToken.None));
    }

    [Fact]
    public async Task Execute_WithoutOperations_Throws()
    {
        await Assert.ThrowsAsync<CommandException>(
            () => BatchExecutor.ExecuteAsync("batch", null!, default, [], CancellationToken.None));
    }

    [Fact]
    public async Task Execute_OverMaximumOperations_Throws()
    {
        var operation = BatchOperationParser.Parse("batch", "{\"op\":\"delete\",\"id\":\"1\"}")[0];
        var operations = Enumerable.Repeat(operation, BatchExecutor.MaxOperations + 1).ToArray();

        await Assert.ThrowsAsync<CommandException>(
            () => BatchExecutor.ExecuteAsync("batch", null!, default, operations, CancellationToken.None));
    }

    [Fact]
    public void CreateResultState_PreservesBatchResultAndRequestCharge()
    {
        var summary = JsonSerializer.SerializeToElement(new
        {
            success = true,
            requestCharge = 4.25,
        });

        var state = BatchExecutor.CreateResultState(summary, "Batch committed.", 4.25);

        Assert.Equal(4.25, state.RequestCharge);
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(4.25, result.GetProperty("requestCharge").GetDouble());
    }

    [Theory]
    [InlineData(1, "1 operation")]
    [InlineData(2, "2 operations")]
    public void BatchMessages_PluralizeNumericOperationCounts(int count, string expected)
    {
        var message = MessageService.GetArgsString("command-batch-cancelled", "count", count);

        Assert.Contains(expected, message);
    }

    [Fact]
    public void Parse_Array_ReturnsAllOperationsInOrder()
    {
        var specs = BatchOperationParser.Parse(
            "batch",
            "[{\"op\":\"create\",\"item\":{\"id\":\"1\"}},{\"op\":\"delete\",\"id\":\"2\"}]");

        Assert.Equal(2, specs.Count);
        Assert.Equal(BatchOperationKind.Create, specs[0].Kind);
        Assert.Equal(BatchOperationKind.Delete, specs[1].Kind);
        Assert.Equal("2", specs[1].Id);
    }

    [Fact]
    public void Parse_SingleObject_ReturnsOneOperation()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"upsert\",\"item\":{\"id\":\"7\"}}");

        Assert.Single(specs);
        Assert.Equal(BatchOperationKind.Upsert, specs[0].Kind);
        Assert.NotNull(specs[0].Item);
    }

    [Fact]
    public void Parse_Create_ExtractsIdFromItem()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"create\",\"item\":{\"id\":\"abc\",\"name\":\"x\"}}");

        Assert.Equal("abc", specs[0].Id);
    }

    [Fact]
    public void Parse_Create_AllowsMissingId()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"create\",\"item\":{\"name\":\"x\"}}");

        Assert.Null(specs[0].Id);
        Assert.Equal(BatchOperationKind.Create, specs[0].Kind);
    }

    [Fact]
    public void Parse_Replace_UsesExplicitIdOverItemId()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"replace\",\"id\":\"explicit\",\"item\":{\"id\":\"inner\"}}");

        Assert.Equal("explicit", specs[0].Id);
        Assert.Equal(BatchOperationKind.Replace, specs[0].Kind);
    }

    [Fact]
    public void Parse_Replace_FallsBackToItemId()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"replace\",\"item\":{\"id\":\"inner\"}}");

        Assert.Equal("inner", specs[0].Id);
    }

    [Fact]
    public void Parse_Replace_MissingId_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"replace\",\"item\":{\"name\":\"x\"}}"));
    }

    [Fact]
    public void Parse_Delete_MissingId_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"delete\"}"));
    }

    [Fact]
    public void Parse_Patch_BuildsPatchOperations()
    {
        var specs = BatchOperationParser.Parse(
            "batch",
            "{\"op\":\"patch\",\"id\":\"1\",\"operations\":[{\"op\":\"set\",\"path\":\"/name\",\"value\":\"x\"},{\"op\":\"incr\",\"path\":\"/n\",\"value\":2}]}");

        Assert.Equal(BatchOperationKind.Patch, specs[0].Kind);
        Assert.Equal("1", specs[0].Id);
        Assert.NotNull(specs[0].PatchOperations);
        Assert.Equal(2, specs[0].PatchOperations!.Count);
    }

    [Fact]
    public void Parse_Patch_MissingOperations_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"patch\",\"id\":\"1\",\"operations\":[]}"));
    }

    [Fact]
    public void Parse_Patch_InvalidOperationEntry_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"patch\",\"id\":\"1\",\"operations\":[{\"op\":\"set\"}]}"));
    }

    [Fact]
    public void Parse_MissingItem_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"create\"}"));
    }

    [Fact]
    public void Parse_ItemNotObject_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"create\",\"item\":\"not-an-object\"}"));
    }

    [Fact]
    public void Parse_MissingOp_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"item\":{\"id\":\"1\"}}"));
    }

    [Fact]
    public void Parse_UnsupportedOp_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"merge\",\"item\":{\"id\":\"1\"}}"));
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{not json"));
    }

    [Fact]
    public void Parse_NonObjectJson_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "42"));
    }

    [Fact]
    public void Parse_NumericId_IsPreservedAsString()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"delete\",\"id\":5}");

        Assert.Equal("5", specs[0].Id);
    }

    [Fact]
    public void Parse_ClonedItem_SurvivesSourceDocumentDisposal()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"create\",\"item\":{\"id\":\"1\",\"name\":\"Ada\"}}");

        // The parser disposes the source JsonDocument internally; the cloned item must remain valid.
        Assert.Equal("Ada", specs[0].Item!.Value.GetProperty("name").GetString());
    }

    [Fact]
    public void Parse_RawOperation_PreservesOriginalOperationJson()
    {
        var specs = BatchOperationParser.Parse(
            "batch",
            "[{\"op\":\"create\",\"item\":{\"id\":\"1\"}},{\"op\":\"patch\",\"id\":\"1\",\"operations\":[{\"op\":\"set\",\"path\":\"/n\",\"value\":1}]}]");

        Assert.Equal("create", specs[0].RawOperation.GetProperty("op").GetString());
        Assert.Equal("1", specs[0].RawOperation.GetProperty("item").GetProperty("id").GetString());
        Assert.Equal("patch", specs[1].RawOperation.GetProperty("op").GetString());
        Assert.Equal(1, specs[1].RawOperation.GetProperty("operations").GetArrayLength());
    }

    private static PendingBatchState CreatePendingBatch()
    {
        return new PendingBatchState("TestDatabase", "TestContainer", "tenant-1", new PartitionKey("tenant-1"));
    }

    private static CosmosClient CreateTestClient()
    {
        return new CosmosClient(
            "https://localhost:8081",
            Convert.ToBase64String(new byte[64]),
            new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
    }
}
