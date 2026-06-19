//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;

internal static class PatchOperationFactory
{
    public static string Normalize(string op) => op.Trim().ToLowerInvariant();

    public static bool IsSupported(string op)
    {
        return op is "set" or "add" or "replace" or "remove" or "incr" or "increment";
    }

    public static PatchOperation Build(string commandName, string opRaw, string path, string? value)
    {
        var op = Normalize(opRaw);

        switch (op)
        {
            case "remove":
                if (value is not null)
                {
                    throw new CommandException(
                        commandName,
                        MessageService.GetString("command-patch-error-unexpected_value_for_remove"));
                }

                return PatchOperation.Remove(path);

            case "add":
                return PatchOperation.Add(path, ParseValue(commandName, op, value));

            case "set":
                return PatchOperation.Set(path, ParseValue(commandName, op, value));

            case "replace":
                return PatchOperation.Replace(path, ParseValue(commandName, op, value));

            case "incr":
            case "increment":
                return BuildIncrementOperation(commandName, path, value);

            default:
                throw new CommandException(
                    commandName,
                    MessageService.GetString(
                        "command-patch-error-unsupported_op",
                        new Dictionary<string, object> { { "op", op } }));
        }
    }

    private static PatchOperation BuildIncrementOperation(string commandName, string path, string? rawValue)
    {
        if (rawValue == null)
        {
            throw new CommandException(
                commandName,
                MessageService.GetString(
                    "command-patch-error-missing_value_for_op",
                    new Dictionary<string, object> { { "op", "incr" } }));
        }

        var trimmed = rawValue.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return PatchOperation.Increment(path, intValue);
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return PatchOperation.Increment(path, doubleValue);
        }

        throw new CommandException(
            commandName,
            MessageService.GetString("command-patch-error-increment_number"));
    }

    private static object? ParseValue(string commandName, string op, string? rawValue)
    {
        if (rawValue == null)
        {
            throw new CommandException(
                commandName,
                MessageService.GetString(
                    "command-patch-error-missing_value_for_op",
                    new Dictionary<string, object> { { "op", op } }));
        }

        var trimmed = rawValue.Trim();
        if (LooksLikeJsonLiteral(trimmed))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                return JsonSerializer.Deserialize<object?>(doc.RootElement.GetRawText());
            }
            catch (JsonException)
            {
                // Fall through and treat as plain string.
            }
        }

        return rawValue;
    }

    private static bool LooksLikeJsonLiteral(string trimmed)
    {
        if (trimmed.Length == 0)
        {
            return false;
        }

        var first = trimmed[0];
        if (first == '{' || first == '[' || first == '"')
        {
            return true;
        }

        if (first == '-' || char.IsDigit(first))
        {
            return true;
        }

        if (string.Equals(trimmed, "true", StringComparison.Ordinal)
            || string.Equals(trimmed, "false", StringComparison.Ordinal)
            || string.Equals(trimmed, "null", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
