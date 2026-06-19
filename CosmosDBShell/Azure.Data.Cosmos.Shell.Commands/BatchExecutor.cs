//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;

internal static class BatchExecutor
{
    internal const int MaxOperations = 100;

    public static async Task<CommandState> ExecuteAsync(
        string commandName,
        Container container,
        PartitionKey partitionKey,
        IReadOnlyList<BatchOperationSpec> operations,
        CancellationToken token)
    {
        if (operations.Count == 0)
        {
            throw new CommandException(commandName, MessageService.GetString("command-batch-error-empty"));
        }

        if (operations.Count > MaxOperations)
        {
            throw new CommandException(
                commandName,
                MessageService.GetArgsString("command-batch-error-too_many", "count", operations.Count.ToString(CultureInfo.InvariantCulture)));
        }

        var batch = container.CreateTransactionalBatch(partitionKey);
        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case BatchOperationKind.Create:
                    batch.CreateItem(operation.Item!.Value);
                    break;

                case BatchOperationKind.Upsert:
                    batch.UpsertItem(operation.Item!.Value);
                    break;

                case BatchOperationKind.Replace:
                    batch.ReplaceItem(operation.Id!, operation.Item!.Value);
                    break;

                case BatchOperationKind.Delete:
                    batch.DeleteItem(operation.Id!);
                    break;

                case BatchOperationKind.Patch:
                    batch.PatchItem(operation.Id!, operation.PatchOperations!);
                    break;
            }
        }

        using var response = await batch.ExecuteAsync(token);

        var summary = BuildSummary(operations, response);

        if (response.IsSuccessStatusCode)
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-batch-success",
                "count",
                operations.Count.ToString(CultureInfo.InvariantCulture),
                "charge",
                response.RequestCharge.ToString("F2", CultureInfo.InvariantCulture)));
        }
        else
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-batch-error-failed",
                "status",
                ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                "charge",
                response.RequestCharge.ToString("F2", CultureInfo.InvariantCulture)));
        }

        return new CommandState { Result = new ShellJson(summary) };
    }

    private static JsonElement BuildSummary(IReadOnlyList<BatchOperationSpec> operations, TransactionalBatchResponse response)
    {
        var operationsArray = new JsonArray();
        for (var i = 0; i < response.Count; i++)
        {
            var result = response[i];
            var node = new JsonObject
            {
                ["index"] = i,
                ["op"] = operations[i].Kind.ToString().ToLowerInvariant(),
                ["statusCode"] = (int)result.StatusCode,
            };

            if (operations[i].Id is { } id)
            {
                node["id"] = id;
            }

            if (!string.IsNullOrEmpty(result.ETag))
            {
                node["etag"] = result.ETag;
            }

            operationsArray.Add(node);
        }

        var root = new JsonObject
        {
            ["success"] = response.IsSuccessStatusCode,
            ["statusCode"] = (int)response.StatusCode,
            ["requestCharge"] = response.RequestCharge,
            ["operationCount"] = operations.Count,
            ["operations"] = operationsArray,
        };

        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }
}
