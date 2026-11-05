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

    private static readonly Regex InterpolatedBearerPattern = new(
        "(?i)(Bearer\\s+)(?=[A-Za-z0-9._~+/=$-])",
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
            var credentialNameIndex = GetCredentialNameIndex(tokens, index);
            var isCredentialValue = credentialNameIndex >= 0;
            if (!isCredentialValue && token.Type is not (TokenType.String or TokenType.InterpolatedString))
            {
                continue;
            }

            if (isCredentialValue)
            {
                var nameStart = Math.Max(offset, tokens[credentialNameIndex].Start);
                var separatorEnd = tokens[index - 1].Start + tokens[index - 1].Length;
                result.Append(MaskHistoryCredentials(value[offset..nameStart]));
                result.Append(value.AsSpan(nameStart, separatorEnd - nameStart));
                result.Append(MaskHistoryCredentials(value[separatorEnd..token.Start]));
                result.Append(ShellLiteral.Quote("[REDACTED]"));
                offset = GetCredentialExpressionEnd(value, token.Start);
                while (index + 1 < tokens.Length && tokens[index + 1].Start < offset)
                {
                    index++;
                }

                continue;
            }
            else
            {
                var raw = value.Substring(token.Start, token.Length);
                result.Append(MaskHistoryCredentials(value[offset..token.Start]));
                if (token.Type == TokenType.InterpolatedString)
                {
                    result.Append(MaskInterpolatedToken(raw, token, lexer, depth));
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

    private static int GetCredentialNameIndex(IReadOnlyList<Token> tokens, int index)
    {
        if (index < 2 || tokens[index - 1].Type is not (TokenType.Assignment or TokenType.Colon))
        {
            return -1;
        }

        var nameIndex = index - 2;
        if (tokens[index - 1].Type == TokenType.Assignment
            && tokens[nameIndex].Type is TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Divide
            && tokens[nameIndex].Start + tokens[nameIndex].Length == tokens[index - 1].Start)
        {
            nameIndex--;
        }

        return nameIndex >= 0
            && IsCredentialName(tokens[nameIndex].Value)
            && (tokens[index].Type is TokenType.String or TokenType.InterpolatedString or TokenType.OpenParenthesis
                || tokens[nameIndex].Value.StartsWith('$') || tokens[index - 1].Type == TokenType.Colon)
            ? nameIndex
            : -1;
    }

    private static int GetCredentialExpressionEnd(string value, int start)
    {
        var lexer = new Lexer(value[start..]);
        var expression = new ExpressionParser(lexer).ParseExpression();
        return lexer.Errors.HasErrors ? value.Length : start + expression.Start + expression.Length;
    }

    private static string MaskInterpolatedToken(string raw, Token token, Lexer lexer, int depth)
    {
        var sourceMap = lexer.GetInterpolatedStringSourceMap(token)!;
        var ranges = new List<(int Start, int End, string Replacement)>();
        var literalRanges = new List<(int Start, int End)>();
        void AddRange(int start, int end, string replacement)
        {
            ranges.Add((
                sourceMap[start] - token.Start,
                end < sourceMap.Count ? sourceMap[end] - token.Start : raw.Length - (raw.EndsWith('"') ? 1 : 0),
                replacement));
        }

        for (var index = 0; index + 1 < token.Value.Length; index++)
        {
            if (token.Value[index] != '$' || token.Value[index + 1] != '(')
            {
                continue;
            }

            var expressionStart = index + 2;
            var expressionEnd = FindInterpolationEnd(token.Value, index);
            var expressionText = token.Value[expressionStart..expressionEnd];
            var expressionTokens = new Lexer(expressionText).Tokenize()
                .Where(item => item.Type is not (TokenType.Eol or TokenType.Comment)).ToArray();
            for (var expressionIndex = 0; expressionIndex < expressionTokens.Length; expressionIndex++)
            {
                var expressionToken = expressionTokens[expressionIndex];
                if (expressionToken.Type is TokenType.String or TokenType.InterpolatedString)
                {
                    literalRanges.Add((expressionStart + expressionToken.Start, expressionStart + expressionToken.Start + expressionToken.Length));
                }

                if (GetCredentialNameIndex(expressionTokens, expressionIndex) >= 0)
                {
                    var start = expressionToken.Start;
                    AddRange(expressionStart + start, expressionStart + GetCredentialExpressionEnd(expressionText, start), "'[REDACTED]'");
                }
                else if (expressionToken.Type is TokenType.String or TokenType.InterpolatedString)
                {
                    var original = expressionText.Substring(expressionToken.Start, expressionToken.Length);
                    var masked = depth >= 15 ? "'[REDACTED]'" : MaskHistory(original, depth + 1);
                    if (masked == original)
                    {
                        continue;
                    }

                    var prefix = 0;
                    while (prefix < original.Length && prefix < masked.Length && original[prefix] == masked[prefix])
                    {
                        prefix++;
                    }

                    var suffix = 0;
                    while (suffix < original.Length - prefix && suffix < masked.Length - prefix
                        && original[^(suffix + 1)] == masked[^(suffix + 1)])
                    {
                        suffix++;
                    }

                    var replacement = masked[prefix..(masked.Length - suffix)]
                        .Replace("\\", "\\\\", StringComparison.Ordinal)
                        .Replace("\"", "\\\"", StringComparison.Ordinal)
                        .Replace("{", "\\{", StringComparison.Ordinal)
                        .Replace("}", "\\}", StringComparison.Ordinal);
                    AddRange(expressionStart + expressionToken.Start + prefix, expressionStart + expressionToken.Start + original.Length - suffix, replacement);
                }
            }

            index = expressionEnd;
        }

        foreach (var pattern in new[] { JwtPattern, InterpolatedBearerPattern, ConnectionCredentialPattern, HistorySignaturePattern })
        {
            foreach (Match match in pattern.Matches(token.Value))
            {
                if (literalRanges.Any(range => match.Index >= range.Start && match.Index < range.End))
                {
                    continue;
                }

                var prefixLength = match.Groups.Count > 1 ? match.Groups[1].Length : 0;
                var endIndex = match.Index + match.Length;
                if (pattern != JwtPattern)
                {
                    endIndex = FindInterpolatedCredentialEnd(token.Value, match.Index + prefixLength, pattern);
                }

                AddRange(match.Index + prefixLength, endIndex, "[REDACTED]");
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

            result.Append(raw.AsSpan(offset, range.Start - offset)).Append(range.Replacement);
            offset = range.End;
        }

        return result.Append(raw.AsSpan(offset)).ToString();
    }

    private static int FindInterpolatedCredentialEnd(string value, int start, Regex pattern)
    {
        for (var index = start; index < value.Length; index++)
        {
            var ch = value[index];
            var isDelimiter = pattern == ConnectionCredentialPattern
                ? ch is ';' or '"' or '\''
                : pattern == HistorySignaturePattern
                    ? ch is '&' or '<' or '>' or '"' or '\'' || char.IsWhiteSpace(ch)
                    : !char.IsAsciiLetterOrDigit(ch) && !"._~+/=$-".Contains(ch);
            if (isDelimiter)
            {
                return index;
            }

            if (ch != '$' || index + 1 >= value.Length || value[index + 1] != '(')
            {
                continue;
            }

            index = FindInterpolationEnd(value, index);
        }

        return value.Length;
    }

    private static int FindInterpolationEnd(string value, int start)
    {
        var expressionStart = start + 1;
        var nesting = 0;
        foreach (var token in new Lexer(value[expressionStart..]).Tokenize())
        {
            if (token.Type == TokenType.OpenParenthesis)
            {
                nesting++;
            }
            else if (token.Type == TokenType.CloseParenthesis && --nesting == 0)
            {
                return expressionStart + token.Start;
            }
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
