// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Best-effort extraction of an error location and friendly message from a
/// Cosmos NoSQL query error payload. Supports a handful of common server
/// shapes:
/// <list type="bullet">
///   <item>JSON with an <c>errors</c> / <c>Errors</c> array of objects
///   containing <c>{ location: { start, end }, message }</c>.</item>
///   <item>JSON with an <c>Errors</c> array of strings (the legacy gateway
///   format), where the offending token is identified with
///   <c>near '&lt;token&gt;'</c>.</item>
///   <item>Plain text messages that include <c>near '&lt;token&gt;'</c>.</item>
/// </list>
/// The locator deliberately falls back to no location rather than guessing
/// when the shape is unfamiliar; callers still display the raw message.
/// </summary>
internal static class QueryErrorLocator
{
    private static readonly Regex NearTokenRegex = new(
        @"near\s+'(?<token>[^']+)'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static QueryErrorLocation? TryLocate(string rawQuery, string rawMessage)
    {
        if (string.IsNullOrEmpty(rawMessage))
        {
            return null;
        }

        var jsonPayload = ExtractJsonPayload(rawMessage);
        if (jsonPayload != null)
        {
            var fromJson = TryLocateFromJson(rawQuery, jsonPayload);
            if (fromJson != null)
            {
                return fromJson;
            }
        }

        return TryLocateFromNearToken(rawQuery, rawMessage);
    }

    private static QueryErrorLocation? TryLocateFromJson(string rawQuery, string jsonPayload)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonPayload);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!TryGetArray(doc.RootElement, "errors", out var errors)
                && !TryGetArray(doc.RootElement, "Errors", out errors))
            {
                return null;
            }

            foreach (var element in errors.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var inner = TryReadStructuredError(rawQuery, element);
                    if (inner != null)
                    {
                        return inner;
                    }
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        var inner = TryLocateFromNearToken(rawQuery, text!);
                        if (inner != null)
                        {
                            return inner;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static QueryErrorLocation? TryReadStructuredError(string rawQuery, JsonElement error)
    {
        string? message = null;
        if (error.TryGetProperty("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String)
        {
            message = messageElement.GetString();
        }

        if (error.TryGetProperty("location", out var location)
            && location.ValueKind == JsonValueKind.Object
            && location.TryGetProperty("start", out var startElement)
            && startElement.ValueKind == JsonValueKind.Number
            && startElement.TryGetInt32(out var start)
            && start >= 0)
        {
            int length = 1;
            if (location.TryGetProperty("end", out var endElement)
                && endElement.ValueKind == JsonValueKind.Number
                && endElement.TryGetInt32(out var end)
                && end > start)
            {
                length = end - start;
            }

            return BuildLocation(rawQuery, start, length, message);
        }

        if (!string.IsNullOrEmpty(message))
        {
            return TryLocateFromNearToken(rawQuery, message!);
        }

        return null;
    }

    private static QueryErrorLocation? TryLocateFromNearToken(string rawQuery, string message)
    {
        var match = NearTokenRegex.Match(message);
        if (!match.Success)
        {
            return null;
        }

        var token = match.Groups["token"].Value;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(rawQuery))
        {
            return null;
        }

        var index = rawQuery.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        return BuildLocation(rawQuery, index, token.Length, message);
    }

    private static QueryErrorLocation BuildLocation(string rawQuery, int start, int length, string? message)
    {
        var (lineIndex, column) = OffsetToLineColumn(rawQuery ?? string.Empty, start);
        return new QueryErrorLocation(
            Line: lineIndex + 1,
            Column: column + 1,
            Length: Math.Max(1, length),
            Message: string.IsNullOrEmpty(message) ? null : message);
    }

    private static (int Line, int Column) OffsetToLineColumn(string text, int absolute)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        absolute = Math.Clamp(absolute, 0, text.Length);
        int line = 0;
        int lastNl = -1;
        for (int i = 0; i < absolute; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastNl = i;
            }
        }

        return (line, absolute - (lastNl + 1));
    }

    private static bool TryGetArray(JsonElement parent, string propertyName, out JsonElement array)
    {
        if (parent.TryGetProperty(propertyName, out array) && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
    }

    private static string? ExtractJsonPayload(string message)
    {
        var firstBrace = message.IndexOf('{');
        if (firstBrace < 0)
        {
            return null;
        }

        // Track depth so we only return a balanced top-level object, ignoring
        // anything before it (e.g. "Message:" prefixes from the SDK) and after.
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (int i = firstBrace; i < message.Length; i++)
        {
            var c = message[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return message.Substring(firstBrace, i - firstBrace + 1);
                    }

                    break;
            }
        }

        return null;
    }
}
