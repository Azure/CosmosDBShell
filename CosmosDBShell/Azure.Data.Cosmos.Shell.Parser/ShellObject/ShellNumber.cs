// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

internal class ShellNumber : ShellObject
{
    public ShellNumber(int value)
        : base(DataType.Number)
    {
        this.Value = value;
    }

    public int Value { get; }

    internal static ShellObject FromJson(JsonElement value)
    {
        return value.TryGetInt32(out var number) ? new ShellNumber(number) : new ShellDecimal(value.GetDouble());
    }

    internal static ShellObject Normalize(ShellObject value)
    {
        return value is ShellJson json && json.Value.ValueKind == JsonValueKind.Number ? FromJson(json.Value) : value;
    }

    public override object ConvertShellObject(DataType type)
    {
        switch (type)
        {
            case DataType.Number:
                return this.Value;
            case DataType.Decimal:
                return (double)this.Value;
            case DataType.Text:
                return this.Value.ToString();
            case DataType.Boolean:
                return this.Value != 0;
            case DataType.Json:
                return JsonDocument.Parse(this.Value.ToString()).RootElement;
            default:
                throw new InvalidOperationException(MessageService.GetArgsString("conversion-error-number-type", "type", type));
        }
    }
}
