//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("batch")]
[CosmosExample("batch run '[{\"op\":\"create\",\"item\":{\"id\":\"1\",\"pk\":\"a\"}},{\"op\":\"delete\",\"id\":\"2\"}]' --partition-key a", Description = "Atomically apply multiple operations in a single transaction")]
[CosmosExample("batch begin --partition-key a", Description = "Start a stateful batch for partition key 'a'")]
[CosmosExample("batch add '{\"op\":\"upsert\",\"item\":{\"id\":\"3\",\"pk\":\"a\"}}'", Description = "Queue an operation onto the active batch")]
[CosmosExample("batch add '{\"op\":\"patch\",\"id\":\"3\",\"operations\":[{\"op\":\"set\",\"path\":\"/status\",\"value\":\"done\"}]}'", Description = "Queue a patch operation onto the active batch")]
[CosmosExample("batch execute", Description = "Execute the queued operations atomically")]
[CosmosExample("batch status", Description = "Show the active batch and its queued operations")]
[CosmosExample("batch cancel", Description = "Discard the active batch")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Batch",
    Description = @"
Executes multiple write operations against a single partition key as one atomic Cosmos DB transactional batch.

Subcommands:
- 'run <json> --partition-key <pk>' parses a JSON array of operations and executes them atomically in one call.
- 'begin --partition-key <pk>' starts a stateful batch; 'add <json>' queues operations; 'execute' commits them; 'cancel' discards them; 'status' reports the pending batch.

Each operation is a JSON object:
- {""op"":""create"",""item"":{...}}
- {""op"":""upsert"",""item"":{...}}
- {""op"":""replace"",""id"":""1"",""item"":{...}}
- {""op"":""delete"",""id"":""3""}
- {""op"":""patch"",""id"":""1"",""operations"":[{""op"":""set"",""path"":""/name"",""value"":""x""}]}

All operations must share the same partition key. A batch holds at least 1 and at most 100 operations. If any operation fails, the whole batch is rolled back.
",
    ReadOnly = false)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class BatchCommand : CosmosCommand
{
    [CosmosParameter("subcommand", RequiredErrorKey = "command-batch-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("data", IsRequired = false)]
    public string? Data { get; init; }

    [CosmosOption("partition-key", "pk")]
    public string? PartitionKeyArgument { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var subcommand = this.Subcommand.Trim().ToLowerInvariant();

        return subcommand switch
        {
            "run" => await this.RunAsync(shell, commandState, token),
            "begin" => await this.BeginAsync(shell, token),
            "add" => this.Add(shell, commandState),
            "execute" or "exec" or "commit" => await this.ExecuteBatchAsync(shell, token),
            "cancel" or "abort" => Cancel(shell),
            "status" => Status(shell),
            "" => throw new CommandException("batch", MessageService.GetString("command-batch-error-missing_subcommand")),
            _ => throw new CommandException(
                "batch",
                MessageService.GetArgsString("command-batch-error-invalid_subcommand", "subcommand", subcommand)),
        };
    }

    private static PartitionKey ParsePartitionKey(string rawValue)
    {
        try
        {
            return CreatePartitionKeyFromArgument(rawValue);
        }
        catch (JsonException ex)
        {
            throw new CommandException("batch", MessageService.GetString("command-batch-error-invalid_pk_json"), ex);
        }
    }

    private static CommandState Cancel(ShellInterpreter shell)
    {
        var batch = shell.CurrentBatch
            ?? throw new CommandException("batch", MessageService.GetString("command-batch-error-not_active"));

        var count = batch.Operations.Count;
        shell.CurrentBatch = null;
        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-batch-cancelled",
            "count",
            count.ToString(CultureInfo.InvariantCulture)));
        return new CommandState();
    }

    private static CommandState Status(ShellInterpreter shell)
    {
        var batch = shell.CurrentBatch;
        JsonObject root;
        if (batch is null)
        {
            root = new JsonObject { ["active"] = false };
        }
        else
        {
            var operations = new JsonArray();
            foreach (var operation in batch.Operations)
            {
                var node = new JsonObject { ["op"] = operation.Kind.ToString().ToLowerInvariant() };
                if (operation.Id is { } id)
                {
                    node["id"] = id;
                }

                operations.Add(node);
            }

            root = new JsonObject
            {
                ["active"] = true,
                ["database"] = batch.DatabaseName,
                ["container"] = batch.ContainerName,
                ["partitionKey"] = batch.PartitionKeyArgument,
                ["operationCount"] = batch.Operations.Count,
                ["operations"] = operations,
            };
        }

        using var document = JsonDocument.Parse(root.ToJsonString());
        return new CommandState { Result = new ShellJson(document.RootElement.Clone()) };
    }

    private async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("batch");
        }

        if (string.IsNullOrWhiteSpace(this.PartitionKeyArgument))
        {
            throw new CommandException("batch", MessageService.GetString("command-batch-error-missing_pk"));
        }

        var json = this.ResolveData(commandState);
        var specs = BatchOperationParser.Parse("batch", json);

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "batch",
            token);

        var partitionKey = ParsePartitionKey(this.PartitionKeyArgument);
        return await BatchExecutor.ExecuteAsync("batch", container, partitionKey, specs, token);
    }

    private async Task<CommandState> BeginAsync(ShellInterpreter shell, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("batch");
        }

        if (shell.CurrentBatch is not null)
        {
            throw new CommandException("batch", MessageService.GetString("command-batch-error-already_active"));
        }

        if (string.IsNullOrWhiteSpace(this.PartitionKeyArgument))
        {
            throw new CommandException("batch", MessageService.GetString("command-batch-error-missing_pk"));
        }

        var (databaseName, containerName, _) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "batch",
            token);

        var partitionKey = ParsePartitionKey(this.PartitionKeyArgument);
        shell.CurrentBatch = new PendingBatchState(databaseName!, containerName!, this.PartitionKeyArgument, partitionKey);

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-batch-begun",
            "database",
            databaseName!,
            "container",
            containerName!));
        return new CommandState();
    }

    private CommandState Add(ShellInterpreter shell, CommandState commandState)
    {
        var batch = shell.CurrentBatch
            ?? throw new CommandException("batch", MessageService.GetString("command-batch-error-not_active"));

        var json = this.ResolveData(commandState);
        var specs = BatchOperationParser.Parse("batch", json);

        if (batch.Operations.Count + specs.Count > BatchExecutor.MaxOperations)
        {
            throw new CommandException(
                "batch",
                MessageService.GetArgsString(
                    "command-batch-error-too_many",
                    "count",
                    (batch.Operations.Count + specs.Count).ToString(CultureInfo.InvariantCulture)));
        }

        batch.Operations.AddRange(specs);
        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-batch-added",
            "count",
            specs.Count.ToString(CultureInfo.InvariantCulture),
            "total",
            batch.Operations.Count.ToString(CultureInfo.InvariantCulture)));
        return new CommandState();
    }

    private async Task<CommandState> ExecuteBatchAsync(ShellInterpreter shell, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("batch");
        }

        var batch = shell.CurrentBatch
            ?? throw new CommandException("batch", MessageService.GetString("command-batch-error-not_active"));

        if (batch.Operations.Count == 0)
        {
            throw new CommandException("batch", MessageService.GetString("command-batch-error-empty"));
        }

        var container = connectedState.Client.GetDatabase(batch.DatabaseName).GetContainer(batch.ContainerName);

        try
        {
            var result = await BatchExecutor.ExecuteAsync("batch", container, batch.PartitionKey, batch.Operations, token);
            shell.CurrentBatch = null;
            return result;
        }
        catch (CosmosException ce)
        {
            throw new CommandException(
                "batch",
                MessageService.GetArgsString(
                    "command-batch-error-execution_failed",
                    "status",
                    ce.StatusCode.ToString(),
                    "message",
                    CommandException.GetDisplayMessage(ce)),
                ce);
        }
    }

    private string ResolveData(CommandState commandState)
    {
        var evaluatedResult = commandState.Result?.ConvertShellObject(DataType.Text);
        var json = this.Data ?? (evaluatedResult as string);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CommandException("batch", MessageService.GetString("command-batch-error-missing_data"));
        }

        return json;
    }
}
