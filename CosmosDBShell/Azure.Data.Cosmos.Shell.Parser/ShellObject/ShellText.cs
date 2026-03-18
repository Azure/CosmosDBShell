// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

internal class ShellText : ShellObject
{
    public ShellText(string text)
        : base(DataType.Text)
    {
        this.Text = text;
    }

    public string Text { get; }

    public override object ConvertShellObject(DataType type)
    {
        return EvaluateString(type, this.Text);
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

                throw new InvalidOperationException($"Cannot convert text '{value}' to boolean");
            case DataType.Number:
                if (int.TryParse(value, out int intValue))
                {
                    return intValue;
                }

                throw new InvalidOperationException($"Cannot convert text '{value}' to number");
            case DataType.Decimal:
                if (double.TryParse(value, out double decimalValue))
                {
                    return decimalValue;
                }

                throw new InvalidOperationException($"Cannot convert text '{value}' to double");
            case DataType.Json:
                try
                {
                    return JsonDocument.Parse(value).RootElement;
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Cannot convert text '{value}' to JSON: {ex.Message}");
                }

            default:
                throw new InvalidOperationException($"Cannot convert text to {type}");
        }
    }
}
