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
    {
        var result = new StringBuilder(value);
        var tokens = new Lexer(value).Tokenize().ToArray();
        foreach (var token in tokens.Reverse())
        {
            if (token.Type is not (TokenType.String or TokenType.InterpolatedString))
            {
                continue;
            }

            var masked = MaskHistoryCredentials(token.Value);
            if (masked == token.Value)
            {
                continue;
            }

            var raw = value.Substring(token.Start, token.Length);
            if (raw.Contains('\\'))
            {
                // Re-encode the cooked value so escaped names and quotes cannot hide credentials.
                result.Remove(token.Start, token.Length).Insert(token.Start, ShellLiteral.Quote(masked));
            }
        }

        return MaskHistoryCredentials(result.ToString());
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
