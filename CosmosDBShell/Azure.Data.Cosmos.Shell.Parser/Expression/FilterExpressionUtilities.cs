// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Linq;
using System.Text.Json;

internal static class FilterExpressionUtilities
{
    public static JsonElement NullElement()
    {
        return JsonSerializer.SerializeToElement<object?>(null);
    }

    public static JsonElement ToJsonElement(ShellObject shellObject)
    {
        switch (shellObject)
        {
            case ShellJson shellJson:
                return shellJson.Value.Clone();
            case ShellText shellText:
                return JsonSerializer.SerializeToElement(shellText.Text);
            case ShellNumber shellNumber:
                return JsonSerializer.SerializeToElement(shellNumber.Value);
            case ShellDecimal shellDecimal:
                return JsonSerializer.SerializeToElement(shellDecimal.Value);
            case ShellBool shellBool:
                return JsonSerializer.SerializeToElement(shellBool.Value);
            case ShellSequence shellSequence:
                return ToJsonArray(shellSequence.Elements);
        }

        var value = shellObject.ConvertShellObject(DataType.Json);
        if (value is JsonElement jsonElement)
        {
            return jsonElement.Clone();
        }

        throw new InvalidOperationException($"Expected JSON value but got {shellObject.GetType().Name}");
    }

    public static JsonElement ToJsonArray(IEnumerable<JsonElement> elements)
    {
        return JsonSerializer.SerializeToElement(elements.Select(static e => e.Clone()).ToArray());
    }

    public static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            JsonValueKind.True => right.ValueKind == JsonValueKind.True,
            JsonValueKind.False => right.ValueKind == JsonValueKind.False,
            JsonValueKind.Number => left.GetDecimal() == right.GetDecimal(),
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Array => left.EnumerateArray().Select(static e => e.Clone()).SequenceEqual(right.EnumerateArray().Select(static e => e.Clone()), JsonElementComparer.Instance),
            JsonValueKind.Object => ObjectEquals(left, right),
            _ => left.GetRawText() == right.GetRawText(),
        };
    }

    public static bool Contains(JsonElement source, JsonElement target)
    {
        if (source.ValueKind == JsonValueKind.String && target.ValueKind == JsonValueKind.String)
        {
            return (source.GetString() ?? string.Empty).Contains(target.GetString() ?? string.Empty, StringComparison.Ordinal);
        }

        if (source.ValueKind == JsonValueKind.Array)
        {
            return source.EnumerateArray().Any(item => JsonEquals(item, target));
        }

        if (source.ValueKind == JsonValueKind.Object && target.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in target.EnumerateObject())
            {
                if (!source.TryGetProperty(property.Name, out var sourceValue) || !Contains(sourceValue, property.Value))
                {
                    return false;
                }
            }

            return true;
        }

        return JsonEquals(source, target);
    }

    public static int Compare(JsonElement left, JsonElement right)
    {
        int leftRank = GetKindRank(left.ValueKind);
        int rightRank = GetKindRank(right.ValueKind);
        if (leftRank != rightRank)
        {
            return leftRank.CompareTo(rightRank);
        }

        return left.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => 0,
            JsonValueKind.False or JsonValueKind.True => left.GetBoolean().CompareTo(right.GetBoolean()),
            JsonValueKind.Number => left.GetDecimal().CompareTo(right.GetDecimal()),
            JsonValueKind.String => string.Compare(left.GetString(), right.GetString(), StringComparison.Ordinal),
            _ => string.Compare(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal),
        };
    }

    private static bool ObjectEquals(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
        var rightProperties = right.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
        if (leftProperties.Length != rightProperties.Length)
        {
            return false;
        }

        for (int i = 0; i < leftProperties.Length; i++)
        {
            if (!string.Equals(leftProperties[i].Name, rightProperties[i].Name, StringComparison.Ordinal) ||
                !JsonEquals(leftProperties[i].Value, rightProperties[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetKindRank(JsonValueKind valueKind)
    {
        return valueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => 0,
            JsonValueKind.False => 1,
            JsonValueKind.True => 2,
            JsonValueKind.Number => 3,
            JsonValueKind.String => 4,
            JsonValueKind.Array => 5,
            JsonValueKind.Object => 6,
            _ => 7,
        };
    }

    private sealed class JsonElementComparer : IEqualityComparer<JsonElement>
    {
        public static readonly JsonElementComparer Instance = new();

        public bool Equals(JsonElement x, JsonElement y)
        {
            return JsonEquals(x, y);
        }

        public int GetHashCode(JsonElement obj)
        {
            return obj.GetRawText().GetHashCode(StringComparison.Ordinal);
        }
    }
}