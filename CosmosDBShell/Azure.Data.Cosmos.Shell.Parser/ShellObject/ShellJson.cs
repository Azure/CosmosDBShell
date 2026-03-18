// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

internal class ShellJson : ShellObject
{
    public ShellJson(JsonElement jsonElement)
        : base(DataType.Json)
    {
        this.Value = jsonElement;
    }

    public JsonElement Value
    {
        get;
    }

    public override object ConvertShellObject(DataType type)
    {
        switch (type)
        {
            case DataType.Json:
                return this.Value;
            case DataType.Text:
                if (this.Value.ValueKind == JsonValueKind.String)
                {
                    return this.Value.GetString() ?? string.Empty;
                }

                return this.Value.GetRawText();
            case DataType.Boolean:
                if (this.Value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (this.Value.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (this.Value.ValueKind == JsonValueKind.String)
                {
                    var strValue = this.Value.GetString() ?? string.Empty;
                    if (string.Equals(strValue, "true", StringComparison.OrdinalIgnoreCase) ||
                        strValue == "1" ||
                        string.Equals(strValue, "yes", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (string.Equals(strValue, "false", StringComparison.OrdinalIgnoreCase) ||
                        strValue == "0" ||
                        string.Equals(strValue, "no", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrEmpty(strValue))
                    {
                        return false;
                    }
                }

                if (this.Value.ValueKind == JsonValueKind.Number)
                {
                    return this.Value.GetInt32() != 0;
                }

                throw new InvalidOperationException($"Cannot convert JSON {this.Value.ValueKind} to boolean");
            case DataType.Number:
                if (this.Value.ValueKind == JsonValueKind.Number)
                {
                    return this.Value.GetInt32();
                }

                if (this.Value.ValueKind == JsonValueKind.String)
                {
                    var strValue = this.Value.GetString() ?? string.Empty;
                    if (int.TryParse(strValue, out int intValue))
                    {
                        return intValue;
                    }
                }

                throw new InvalidOperationException($"Cannot convert JSON {this.Value.ValueKind} to number");
            case DataType.Decimal:
                if (this.Value.ValueKind == JsonValueKind.Number)
                {
                    return this.Value.GetDecimal();
                }

                if (this.Value.ValueKind == JsonValueKind.String)
                {
                    var strValue = this.Value.GetString() ?? string.Empty;
                    if (decimal.TryParse(strValue, out decimal decimalValue))
                    {
                        return decimalValue;
                    }
                }

                throw new InvalidOperationException($"Cannot convert JSON {this.Value.ValueKind} to decimal");
            default:
                throw new InvalidOperationException($"Cannot convert JSON to {type}");
        }
    }
}
