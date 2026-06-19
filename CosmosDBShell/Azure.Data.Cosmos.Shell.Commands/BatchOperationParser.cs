//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;

internal static class BatchOperationParser
{
    public static List<BatchOperationSpec> Parse(string commandName, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CommandException(
                commandName,
                MessageService.GetArgsString("command-batch-error-invalid_json", "message", ex.Message),
                ex);
        }

        using (document)
        {
            var root = document.RootElement;
            var specs = new List<BatchOperationSpec>();

            switch (root.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var element in root.EnumerateArray())
                    {
                        specs.Add(ParseOne(commandName, element));
                    }

                    break;

                case JsonValueKind.Object:
                    specs.Add(ParseOne(commandName, root));
                    break;

                default:
                    throw new CommandException(commandName, MessageService.GetString("command-batch-error-not_object"));
            }

            return specs;
        }
    }

    private static BatchOperationSpec ParseOne(string commandName, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CommandException(commandName, MessageService.GetString("command-batch-error-not_object"));
        }

        if (!element.TryGetProperty("op", out var opElement) || opElement.ValueKind != JsonValueKind.String)
        {
            throw new CommandException(commandName, MessageService.GetString("command-batch-error-missing_op"));
        }

        var op = opElement.GetString()!.Trim().ToLowerInvariant();
        var raw = element.Clone();

        switch (op)
        {
            case "create":
                return new BatchOperationSpec { Kind = BatchOperationKind.Create, Item = RequireItem(commandName, element, op), Id = ExtractItemId(element), RawOperation = raw };

            case "upsert":
                return new BatchOperationSpec { Kind = BatchOperationKind.Upsert, Item = RequireItem(commandName, element, op), Id = ExtractItemId(element), RawOperation = raw };

            case "replace":
                var item = RequireItem(commandName, element, op);
                var replaceId = ExtractExplicitId(element) ?? ExtractItemId(element);
                if (string.IsNullOrEmpty(replaceId))
                {
                    throw new CommandException(commandName, MessageService.GetArgsString("command-batch-error-missing_id", "op", op));
                }

                return new BatchOperationSpec { Kind = BatchOperationKind.Replace, Item = item, Id = replaceId, RawOperation = raw };

            case "delete":
                var deleteId = ExtractExplicitId(element);
                if (string.IsNullOrEmpty(deleteId))
                {
                    throw new CommandException(commandName, MessageService.GetArgsString("command-batch-error-missing_id", "op", op));
                }

                return new BatchOperationSpec { Kind = BatchOperationKind.Delete, Id = deleteId, RawOperation = raw };

            case "patch":
                var patchId = ExtractExplicitId(element);
                if (string.IsNullOrEmpty(patchId))
                {
                    throw new CommandException(commandName, MessageService.GetArgsString("command-batch-error-missing_id", "op", op));
                }

                return new BatchOperationSpec { Kind = BatchOperationKind.Patch, Id = patchId, PatchOperations = ParsePatchOperations(commandName, element), RawOperation = raw };

            default:
                throw new CommandException(
                    commandName,
                    MessageService.GetArgsString("command-batch-error-unsupported_op", "op", op));
        }
    }

    private static JsonElement RequireItem(string commandName, JsonElement element, string op)
    {
        if (!element.TryGetProperty("item", out var itemElement))
        {
            throw new CommandException(commandName, MessageService.GetArgsString("command-batch-error-missing_item", "op", op));
        }

        if (itemElement.ValueKind != JsonValueKind.Object)
        {
            throw new CommandException(commandName, MessageService.GetArgsString("command-batch-error-invalid_item", "op", op));
        }

        return itemElement.Clone();
    }

    private static string? ExtractExplicitId(JsonElement element)
    {
        if (element.TryGetProperty("id", out var idElement))
        {
            return idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString(),
                JsonValueKind.Number => idElement.GetRawText(),
                _ => null,
            };
        }

        return null;
    }

    private static string? ExtractItemId(JsonElement element)
    {
        if (element.TryGetProperty("item", out var itemElement)
            && itemElement.ValueKind == JsonValueKind.Object
            && itemElement.TryGetProperty("id", out var idElement))
        {
            return idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString(),
                JsonValueKind.Number => idElement.GetRawText(),
                _ => null,
            };
        }

        return null;
    }

    private static List<PatchOperation> ParsePatchOperations(string commandName, JsonElement element)
    {
        if (!element.TryGetProperty("operations", out var operationsElement) || operationsElement.ValueKind != JsonValueKind.Array)
        {
            throw new CommandException(commandName, MessageService.GetString("command-batch-error-missing_patch_ops"));
        }

        var operations = new List<PatchOperation>();
        foreach (var patchElement in operationsElement.EnumerateArray())
        {
            if (patchElement.ValueKind != JsonValueKind.Object
                || !patchElement.TryGetProperty("op", out var patchOp)
                || patchOp.ValueKind != JsonValueKind.String
                || !patchElement.TryGetProperty("path", out var patchPath)
                || patchPath.ValueKind != JsonValueKind.String)
            {
                throw new CommandException(commandName, MessageService.GetString("command-batch-error-invalid_patch_op"));
            }

            var value = patchElement.TryGetProperty("value", out var valueElement)
                ? valueElement.GetRawText()
                : null;

            operations.Add(PatchOperationFactory.Build(commandName, patchOp.GetString()!, patchPath.GetString()!, value));
        }

        if (operations.Count == 0)
        {
            throw new CommandException(commandName, MessageService.GetString("command-batch-error-missing_patch_ops"));
        }

        return operations;
    }
}
