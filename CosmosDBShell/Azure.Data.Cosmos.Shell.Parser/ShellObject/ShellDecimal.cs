// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Globalization;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

internal class ShellDecimal : ShellObject
{
    public ShellDecimal(double value)
        : base(DataType.Decimal)
    {
        this.Value = value;
    }

    public double Value { get; }

    public override object ConvertShellObject(DataType type)
    {
        switch (type)
        {
            case DataType.Number:
                return (int)this.Value;
            case DataType.Decimal:
                return this.Value;
            case DataType.Text:
                return this.Value.ToString(CultureInfo.InvariantCulture);
            case DataType.Boolean:
                return this.Value != 0;
            case DataType.Json:
                using (JsonDocument document = JsonDocument.Parse(this.Value.ToString(CultureInfo.InvariantCulture)))
                {
                    return document.RootElement.Clone();
                }

            default:
                throw new InvalidOperationException($"Cannot convert decimal to {type}");
        }
    }
}
