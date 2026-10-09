// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Validates bulk operations against the batch operation schema and rewrites them into a
/// canonical form with an explicit, complete partition key. Normalizing an already
/// normalized operation returns identical text, so saved plans hash identically on resume.
/// </summary>
internal static class BulkOperationNormalizer
{
    internal const int MaxPatchOperations = 10;

    internal static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static BulkOperation Normalize(JsonElement element, IReadOnlyList<string> partitionKeyPaths, string? defaultPartitionKey, long index)
    {
        try
        {
            return NormalizeCore(element, partitionKeyPaths, defaultPartitionKey, index);
        }
        catch (CommandException ex)
        {
            throw new CommandException(
                "bulk",
                MessageService.GetArgsString("command-bulk-error-operation", "index", index + 1, "message", ex.Message),
                ex);
        }
    }

    public static void ValidatePatchOperations(JsonElement operations)
    {
        if (operations.ValueKind != JsonValueKind.Array)
        {
            throw Error("command-batch-error-missing_patch_ops");
        }

        var parsed = BatchOperationParser.ParsePatchOperations("bulk", JsonSerializer.SerializeToElement(new { operations }));
        if (parsed.Count > MaxPatchOperations)
        {
            throw Error("command-bulk-error-patch_count");
        }

        foreach (var entry in operations.EnumerateArray())
        {
            if (!entry.GetProperty("path").GetString()!.StartsWith('/'))
            {
                throw Error("command-bulk-error-patch_path");
            }
        }
    }

    public static string CanonicalKeyFromArgument(string raw, int componentCount)
    {
        var trimmed = raw.Trim();
        JsonElement element;
        try
        {
            element = LooksLikeJsonLiteral(trimmed)
                ? JsonSerializer.Deserialize<JsonElement>(trimmed)
                : JsonSerializer.SerializeToElement(raw);
        }
        catch (JsonException)
        {
            element = JsonSerializer.SerializeToElement(raw);
        }

        var key = CanonicalKey(element);
        _ = ParseKey(key, componentCount);
        return key;
    }

    public static PartitionKey ParseKey(string canonicalKey, int? componentCount = null)
    {
        using var document = JsonDocument.Parse(canonicalKey);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || (componentCount.HasValue && root.GetArrayLength() != componentCount.Value))
        {
            throw Error("command-bulk-error-partition_key");
        }

        var length = root.GetArrayLength();
        if (length == 0)
        {
            return PartitionKey.None;
        }

        if (length == 1)
        {
            var single = root[0];
            return single.ValueKind switch
            {
                JsonValueKind.String => new PartitionKey(single.GetString()),
                JsonValueKind.Number => new PartitionKey(single.GetDouble()),
                JsonValueKind.True or JsonValueKind.False => new PartitionKey(single.GetBoolean()),
                JsonValueKind.Null => PartitionKey.Null,
                JsonValueKind.Object when IsUndefined(single) => PartitionKey.None,
                _ => throw Error("command-bulk-error-partition_key"),
            };
        }

        var builder = new PartitionKeyBuilder();
        foreach (var component in root.EnumerateArray())
        {
            switch (component.ValueKind)
            {
                case JsonValueKind.String:
                    builder.Add(component.GetString());
                    break;
                case JsonValueKind.Number:
                    builder.Add(component.GetDouble());
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    builder.Add(component.GetBoolean());
                    break;
                case JsonValueKind.Null:
                    builder.AddNullValue();
                    break;
                case JsonValueKind.Object when IsUndefined(component):
                    builder.AddNoneType();
                    break;
                default:
                    throw Error("command-bulk-error-partition_key");
            }
        }

        return builder.Build();
    }

    private static BulkOperation NormalizeCore(JsonElement element, IReadOnlyList<string> paths, string? defaultKey, long index)
    {
        var spec = BatchOperationParser.ParseOne("bulk", element);
        var op = spec.Kind.ToString().ToLowerInvariant();
        var id = GetId(element, spec);
        var key = GetPartitionKey(element, spec, paths, defaultKey);

        string? ifMatch = null;
        if (element.TryGetProperty("ifMatch", out var tag))
        {
            if (spec.Kind == BatchOperationKind.Create || tag.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(tag.GetString()))
            {
                throw Error("command-bulk-error-if_match");
            }

            ifMatch = tag.GetString();
        }

        JsonElement? patchOperations = null;
        if (spec.Kind == BatchOperationKind.Patch)
        {
            patchOperations = element.GetProperty("operations");
            ValidatePatchOperations(patchOperations.Value);
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("op", op);
            writer.WriteString("id", id);
            writer.WritePropertyName("partitionKey");
            writer.WriteRawValue(key, skipInputValidation: true);
            if (ifMatch != null)
            {
                writer.WriteString("ifMatch", ifMatch);
            }

            if (spec.Item is { } item)
            {
                writer.WritePropertyName("item");
                item.WriteTo(writer);
            }

            if (patchOperations is { } operations)
            {
                writer.WritePropertyName("operations");
                operations.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return new BulkOperation(index, op, id, key, Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static string GetId(JsonElement element, BatchOperationSpec spec)
    {
        string? explicitId = null;
        if (element.TryGetProperty("id", out var idElement))
        {
            if (idElement.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(idElement.GetString()))
            {
                throw Error("command-bulk-error-id");
            }

            explicitId = idElement.GetString();
        }

        if (spec.Item is { } item)
        {
            if (!item.TryGetProperty("id", out var itemId) || itemId.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(itemId.GetString())
                || (explicitId != null && explicitId != itemId.GetString()))
            {
                throw Error("command-bulk-error-id");
            }

            return itemId.GetString()!;
        }

        return explicitId ?? throw Error("command-bulk-error-id");
    }

    private static string GetPartitionKey(JsonElement element, BatchOperationSpec spec, IReadOnlyList<string> paths, string? defaultKey)
    {
        var explicitKey = element.TryGetProperty("partitionKey", out var supplied) ? CanonicalKey(supplied) : defaultKey;
        if (spec.Item is { } item)
        {
            var derived = DeriveKey(item, paths);
            var derivedKey = ParseKey(derived, paths.Count);
            if (explicitKey != null && !ParseKey(explicitKey, paths.Count).Equals(derivedKey))
            {
                throw Error("command-bulk-error-partition_key_mismatch");
            }

            return derived;
        }

        if (explicitKey == null)
        {
            throw Error("command-bulk-error-partition_key");
        }

        _ = ParseKey(explicitKey, paths.Count);
        return explicitKey;
    }

    private static string CanonicalKey(JsonElement key)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartArray();
            if (key.ValueKind == JsonValueKind.Array)
            {
                foreach (var component in key.EnumerateArray())
                {
                    component.WriteTo(writer);
                }
            }
            else
            {
                key.WriteTo(writer);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string DeriveKey(JsonElement item, IReadOnlyList<string> paths)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartArray();
            foreach (var path in paths)
            {
                var current = item;
                var found = true;
                foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    current.WriteTo(writer);
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static bool IsUndefined(JsonElement element) => !element.EnumerateObject().Any();

    private static bool LooksLikeJsonLiteral(string trimmed)
    {
        if (trimmed.Length == 0)
        {
            return false;
        }

        var first = trimmed[0];
        return first is '[' or '{' or '"' or '-' || char.IsDigit(first)
            || trimmed is "true" or "false" or "null";
    }

    private static CommandException Error(string key) => new("bulk", MessageService.GetString(key));
}
