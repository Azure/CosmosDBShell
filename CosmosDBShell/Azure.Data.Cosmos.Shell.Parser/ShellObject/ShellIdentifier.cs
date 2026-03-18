// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

internal class ShellIdentifier : ShellObject
{
    public ShellIdentifier(string value)
        : base(DataType.Text)
    {
        this.Value = value;
    }

    public string Value { get; }

    public override object ConvertShellObject(DataType type)
    {
        return EvaluateString(type, this.Value);
    }

    internal static object EvaluateString(DataType type, string value)
    {
        switch (type)
        {
            case DataType.Text:
                return value;
            case DataType.Boolean:
                // Convert common string representations to boolean
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                    value == "1" ||
                    string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                    value == "0" ||
                    string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrEmpty(value))
                {
                    return false;
                }

                throw new InvalidOperationException($"Cannot convert identifier '{value}' to boolean");
            case DataType.Number:
                if (int.TryParse(value, out int intValue))
                {
                    return intValue;
                }

                throw new InvalidOperationException($"Cannot convert identifier '{value}' to number");
            case DataType.Decimal:
                if (double.TryParse(value, out double decimalValue))
                {
                    return decimalValue;
                }

                throw new InvalidOperationException($"Cannot convert identifier '{value}' to double");
            case DataType.Json:
                try
                {
                    using (JsonDocument document = JsonDocument.Parse(value))
                    {
                        return document.RootElement.Clone();
                    }
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Cannot convert identifier '{value}' to JSON: {ex.Message}");
                }

            default:
                throw new InvalidOperationException($"Cannot convert identifier to {type}");
        }
    }
}
