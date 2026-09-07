// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Azure.Data.Cosmos.Shell.Core;

internal class ShellDecimal : ShellObject
{
    public ShellDecimal(double value)
        : base(DataType.Decimal)
    {
        this.Value = value;
    }

    public double Value { get; }

    internal static JsonSerializerOptions JsonSerializationOptions { get; } = new()
    {
        Converters = { new DecimalJsonConverter() },
    };

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
                return JsonSerializer.SerializeToElement(this.Value, JsonSerializationOptions);

            default:
                throw new InvalidOperationException($"Cannot convert decimal to {type}");
        }
    }

    private sealed class DecimalJsonConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetDouble();
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            var number = JsonSerializer.Serialize(value);
            if (number.IndexOfAny(['.', 'e', 'E']) < 0)
            {
                number += ".0";
            }

            writer.WriteRawValue(number);
        }
    }
}
