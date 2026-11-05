// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Text;
using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

internal static class SecretRedactor
{
    private static readonly Regex JwtPattern = new(
        "eyJ[A-Za-z0-9_-]+\\.eyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        "(?i)(Bearer\\s+)[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled);

    private static readonly Regex ConnectionCredentialPattern = new(
        """(?i)(\b(?:AccountKey|SharedAccessKey|ResourceToken)["']?\s*[=:]\s*["']?)[^;"']+""",
        RegexOptions.Compiled);

    private static readonly Regex KeyTokenPattern = new(
        "(?i)(AccountKey|SharedAccessKey|ResourceToken|SharedAccessSignature|password|passwd|pwd|sig|token|secret|apikey|api[_-]?key)(\\s*[=:]\\s*\"?)[^;\\s\"',}&]+",
        RegexOptions.Compiled);

    private static readonly Regex HistorySignaturePattern = new(
        """(?i)([?&]sig=)[^&\s"'<>]+""",
        RegexOptions.Compiled);

    internal static string MaskHistory(string value)
        => MaskHistory(value, 0);

    private static string MaskHistory(string value, int depth)
    {
        if (depth >= 16)
        {
            return "[REDACTED]";
        }

        var result = new StringBuilder(value.Length);
        var lexer = new Lexer(value);
        var tokens = lexer.Tokenize().Where(token => token.Type is not (TokenType.Eol or TokenType.Comment)).ToArray();
        var offset = 0;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Type is not (TokenType.String or TokenType.InterpolatedString))
            {
                continue;
            }

            var raw = value.Substring(token.Start, token.Length);
            var isCredentialValue = index >= 2
                && tokens[index - 1].Value is ":" or "="
                && IsCredentialName(tokens[index - 2].Value);
            if (isCredentialValue)
            {
                var nameStart = Math.Max(offset, tokens[index - 2].Start);
                var separatorEnd = tokens[index - 1].Start + tokens[index - 1].Length;
                result.Append(MaskHistoryCredentials(value[offset..nameStart]));
                result.Append(value.AsSpan(nameStart, separatorEnd - nameStart));
                result.Append(MaskHistoryCredentials(value[separatorEnd..token.Start]));
                result.Append(ShellLiteral.Quote("[REDACTED]"));
            }
            else
            {
                result.Append(MaskHistoryCredentials(value[offset..token.Start]));
                if (token.Type == TokenType.InterpolatedString)
                {
                    result.Append(MaskInterpolatedToken(raw, token, lexer));
                }
                else
                {
                    var masked = MaskHistory(token.Value, depth + 1);
                    if (masked == token.Value)
                    {
                        result.Append(raw);
                    }
                    else if (raw.StartsWith('\''))
                    {
                        result.Append('\'').Append(masked.Replace("'", "''", StringComparison.Ordinal));
                        if (raw.Length > 1 && raw.EndsWith('\''))
                        {
                            result.Append('\'');
                        }
                    }
                    else
                    {
                        var quoted = ShellLiteral.Quote(masked);
                        result.Append(raw.Length > 1 && raw.EndsWith('"') ? quoted : quoted[..^1]);
                    }
                }
            }

            offset = token.Start + token.Length;
        }

        result.Append(MaskHistoryCredentials(value[offset..]));
        return result.ToString();
    }

    private static bool IsCredentialName(string value)
    {
        if (value.StartsWith('$'))
        {
            value = value[1..];
        }

        return value.Equals("AccountKey", StringComparison.OrdinalIgnoreCase)
            || value.Equals("SharedAccessKey", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ResourceToken", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskInterpolatedToken(string raw, Token token, Lexer lexer)
    {
        var sourceMap = lexer.GetInterpolatedStringSourceMap(token)!;
        var ranges = new List<(int Start, int End)>();
        foreach (var pattern in new[] { JwtPattern, BearerPattern, ConnectionCredentialPattern, HistorySignaturePattern })
        {
            foreach (Match match in pattern.Matches(token.Value))
            {
                var prefixLength = match.Groups.Count > 1 ? match.Groups[1].Length : 0;
                var start = sourceMap[match.Index + prefixLength] - token.Start;
                var endIndex = match.Index + match.Length;
                if (pattern == ConnectionCredentialPattern || pattern == HistorySignaturePattern)
                {
                    endIndex = FindInterpolatedCredentialEnd(token.Value, match.Index + prefixLength, pattern == HistorySignaturePattern);
                }

                var end = endIndex < sourceMap.Count
                    ? sourceMap[endIndex] - token.Start
                    : raw.Length - (raw.EndsWith('"') ? 1 : 0);
                ranges.Add((start, end));
            }
        }

        var result = new StringBuilder(raw.Length);
        var offset = 0;
        foreach (var range in ranges.OrderBy(range => range.Start).ThenByDescending(range => range.End))
        {
            if (range.Start < offset)
            {
                continue;
            }

            result.Append(raw.AsSpan(offset, range.Start - offset)).Append("[REDACTED]");
            offset = range.End;
        }

        return result.Append(raw.AsSpan(offset)).ToString();
    }

    private static int FindInterpolatedCredentialEnd(string value, int start, bool isSignature)
    {
        for (var index = start; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch is '"' or '\'' || (isSignature ? ch is '&' or '<' or '>' || char.IsWhiteSpace(ch) : ch == ';'))
            {
                return index;
            }

            if (ch != '$' || index + 1 >= value.Length || value[index + 1] != '(')
            {
                continue;
            }

            var expressionStart = index + 1;
            var nesting = 0;
            var expressionEnd = -1;
            foreach (var expressionToken in new Lexer(value[expressionStart..]).Tokenize())
            {
                if (expressionToken.Type == TokenType.OpenParenthesis)
                {
                    nesting++;
                }
                else if (expressionToken.Type == TokenType.CloseParenthesis && --nesting == 0)
                {
                    expressionEnd = expressionStart + expressionToken.Start;
                    break;
                }
            }

            if (expressionEnd < 0)
            {
                return value.Length;
            }

            index = expressionEnd;
        }

        return value.Length;
    }

    private static string MaskHistoryCredentials(string value)
    {
        value = JwtPattern.Replace(value, "[REDACTED]");
        value = BearerPattern.Replace(value, match => match.Groups[1].Value + "[REDACTED]");
        value = ConnectionCredentialPattern.Replace(value, match => match.Groups[1].Value + "[REDACTED]");
        return HistorySignaturePattern.Replace(value, match => match.Groups[1].Value + "[REDACTED]");
    }

    internal static string MaskSecrets(string value, IReadOnlyList<string> secrets, string replacement)
    {
        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret))
            {
                continue;
            }

            foreach (var form in new[] { secret, Uri.EscapeDataString(secret) })
            {
                value = Regex.Replace(value, Regex.Escape(form), _ => replacement + ":secret", RegexOptions.IgnoreCase);
            }
        }

        value = JwtPattern.Replace(value, _ => replacement + ":jwt");
        value = BearerPattern.Replace(value, match => match.Groups[1].Value + replacement);
        value = ConnectionCredentialPattern.Replace(value, match => match.Groups[1].Value + replacement);
        return KeyTokenPattern.Replace(value, match => match.Groups[1].Value + match.Groups[2].Value + replacement);
    }
}
