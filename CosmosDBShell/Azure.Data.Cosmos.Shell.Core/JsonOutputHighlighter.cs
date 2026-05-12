// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System.Text;
using System.Text.Json;
using Spectre.Console;

/// <summary>
/// Produces a Spectre.Console markup string for a <see cref="JsonElement"/>, applying the
/// JSON colors defined in <see cref="Theme"/>. The resulting layout matches the indented
/// output produced by <see cref="System.Text.Json.Utf8JsonWriter"/> with <c>Indented = true</c>.
/// </summary>
internal static class JsonOutputHighlighter
{
    private const int IndentSize = 2;

    public static string BuildMarkup(JsonElement element)
    {
        var sb = new StringBuilder();
        WriteValue(sb, element, indent: 0);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, JsonElement element, int indent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(sb, element, indent);
                break;
            case JsonValueKind.Array:
                WriteArray(sb, element, indent);
                break;
            case JsonValueKind.String:
                sb.Append(Theme.FormatJsonString(EncodeJsonString(element.GetString() ?? string.Empty)));
                break;
            case JsonValueKind.Number:
                sb.Append(Theme.FormatJsonNumber(element.GetRawText()));
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                sb.Append(Theme.FormatJsonBoolean(element.GetRawText()));
                break;
            case JsonValueKind.Null:
                sb.Append(Theme.FormatJsonNull("null"));
                break;
            default:
                sb.Append(Markup.Escape(element.GetRawText()));
                break;
        }
    }

    private static void WriteObject(StringBuilder sb, JsonElement element, int indent)
    {
        var enumerator = element.EnumerateObject();
        if (!enumerator.MoveNext())
        {
            sb.Append(Theme.FormatBracket("{", indent));
            sb.Append(Theme.FormatBracket("}", indent));
            return;
        }

        sb.Append(Theme.FormatBracket("{", indent));
        sb.Append('\n');

        var first = true;
        do
        {
            if (!first)
            {
                sb.Append(Theme.FormatJsonBracket(","));
                sb.Append('\n');
            }

            first = false;

            AppendIndent(sb, indent + 1);
            sb.Append(Theme.FormatJsonProperty(EncodeJsonString(enumerator.Current.Name)));
            sb.Append(Theme.FormatJsonBracket(":"));
            sb.Append(' ');
            WriteValue(sb, enumerator.Current.Value, indent + 1);
        }
        while (enumerator.MoveNext());

        sb.Append('\n');
        AppendIndent(sb, indent);
        sb.Append(Theme.FormatBracket("}", indent));
    }

    private static void WriteArray(StringBuilder sb, JsonElement element, int indent)
    {
        var enumerator = element.EnumerateArray();
        if (!enumerator.MoveNext())
        {
            sb.Append(Theme.FormatBracket("[", indent));
            sb.Append(Theme.FormatBracket("]", indent));
            return;
        }

        sb.Append(Theme.FormatBracket("[", indent));
        sb.Append('\n');

        var first = true;
        do
        {
            if (!first)
            {
                sb.Append(Theme.FormatJsonBracket(","));
                sb.Append('\n');
            }

            first = false;

            AppendIndent(sb, indent + 1);
            WriteValue(sb, enumerator.Current, indent + 1);
        }
        while (enumerator.MoveNext());

        sb.Append('\n');
        AppendIndent(sb, indent);
        sb.Append(Theme.FormatBracket("]", indent));
    }

    private static void AppendIndent(StringBuilder sb, int level)
    {
        sb.Append(' ', level * IndentSize);
    }

    /// <summary>
    /// Serializes the value as a JSON string literal (with surrounding quotes and JSON escapes)
    /// so that embedded quotes, backslashes, and control characters render correctly.
    /// </summary>
    private static string EncodeJsonString(string value)
    {
        return JsonSerializer.Serialize(value);
    }
}
