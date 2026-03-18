// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

internal class ShellBool : ShellObject
{
    public ShellBool(bool value)
        : base(DataType.Boolean)
    {
        this.Value = value;
    }

    public bool Value { get; }

    public override object ConvertShellObject(DataType type)
    {
        switch (type)
        {
            case DataType.Boolean:
                return this.Value;
            case DataType.Text:
                return this.Value.ToString().ToLower();
            case DataType.Number:
                return this.Value ? 1 : 0;
            case DataType.Decimal:
                return this.Value ? 1.0 : 0.0;
            default:
                throw new InvalidOperationException($"Cannot convert boolean to {type}");
        }
    }
}
