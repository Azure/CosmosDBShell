// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System.Text;
using Spectre.Console;

/// <summary>
/// Produces a Spectre.Console markup string with basic JavaScript syntax highlighting,
/// applying the colors defined in <see cref="Theme"/>. Used when displaying the bodies of
/// server-side scripts (stored procedures, triggers, user-defined functions), which Cosmos DB
/// stores as JavaScript. The scan is intentionally lightweight: it recognizes comments,
/// string and template literals, numbers, and a fixed keyword set; everything else is emitted
/// as plain (escaped) text.
/// </summary>
internal static class JavaScriptOutputHighlighter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "arguments", "async", "await", "boolean", "break", "byte", "case", "catch",
        "char", "class", "const", "continue", "debugger", "default", "delete", "do", "double",
        "else", "enum", "export", "extends", "false", "final", "finally", "float", "for",
        "function", "goto", "if", "implements", "import", "in", "instanceof", "int", "interface",
        "let", "long", "native", "new", "null", "of", "package", "private", "protected", "public",
        "return", "short", "static", "super", "switch", "synchronized", "this", "throw", "throws",
        "transient", "true", "try", "typeof", "undefined", "var", "void", "volatile", "while",
        "with", "yield",
    };

    public static string BuildMarkup(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(source.Length + 32);
        int i = 0;
        int n = source.Length;

        while (i < n)
        {
            char c = source[i];

            // Line comment: // ... end of line
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                int start = i;
                while (i < n && source[i] != '\n')
                {
                    i++;
                }

                sb.Append(Theme.FormatMuted(source[start..i]));
                continue;
            }

            // Block comment: /* ... */
            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i < n && !(source[i] == '*' && i + 1 < n && source[i + 1] == '/'))
                {
                    i++;
                }

                if (i < n)
                {
                    i += 2; // consume the closing */
                }

                sb.Append(Theme.FormatMuted(source[start..Math.Min(i, n)]));
                continue;
            }

            // String or template literal
            if (c == '"' || c == '\'' || c == '`')
            {
                char quote = c;
                int start = i;
                i++;
                while (i < n)
                {
                    if (source[i] == '\\' && i + 1 < n)
                    {
                        i += 2;
                        continue;
                    }

                    if (source[i] == quote)
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                sb.Append(Theme.FormatStringLiteral(source[start..Math.Min(i, n)]));
                continue;
            }

            // Number (decimal, hex, with simple suffixes)
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(source[i + 1])))
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(source[i]) || source[i] == '.' || source[i] == '_'))
                {
                    i++;
                }

                sb.Append(Theme.FormatJsonNumber(source[start..i]));
                continue;
            }

            // Identifier or keyword
            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '$'))
                {
                    i++;
                }

                var word = source[start..i];
                sb.Append(Keywords.Contains(word) ? Theme.FormatKeyword(word) : Markup.Escape(word));
                continue;
            }

            // Punctuation and whitespace are emitted verbatim (escaped).
            sb.Append(Markup.Escape(c.ToString()));
            i++;
        }

        return sb.ToString();
    }
}
