// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Canonical parsing for user-supplied output format tokens. This is the single
/// source of truth for the accepted format vocabulary shared by the global
/// <c>--output</c> option, the per-command <c>--format</c> option, and
/// <see cref="CommandState.SetFormat"/>.
/// </summary>
internal static class OutputFormats
{
    /// <summary>
    /// Attempts to parse a format token into an <see cref="OutputFormat"/> value.
    /// Accepted tokens (case-insensitive): <c>user</c>, <c>json</c>/<c>js</c>,
    /// <c>csv</c>, and <c>table</c>/<c>tbl</c>.
    /// </summary>
    /// <param name="value">The token to parse. Null or whitespace yields <see langword="false"/>.</param>
    /// <param name="format">The parsed format when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the token was recognized; otherwise <see langword="false"/>.</returns>
    internal static bool TryParse(string? value, out OutputFormat format)
    {
        format = OutputFormat.User;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "user":
                format = OutputFormat.User;
                return true;
            case "json":
            case "js":
                format = OutputFormat.JSon;
                return true;
            case "csv":
                format = OutputFormat.CSV;
                return true;
            case "table":
            case "tbl":
                format = OutputFormat.Table;
                return true;
            default:
                return false;
        }
    }
}
