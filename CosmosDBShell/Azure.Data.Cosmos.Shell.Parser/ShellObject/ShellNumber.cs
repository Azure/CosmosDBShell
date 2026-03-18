// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

internal class ShellNumber : ShellObject
{
    public ShellNumber(int value)
        : base(DataType.Number)
    {
        this.Value = value;
    }

    public int Value { get; }

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
                throw new InvalidOperationException($"Cannot convert number to {type}");
        }
    }
}
