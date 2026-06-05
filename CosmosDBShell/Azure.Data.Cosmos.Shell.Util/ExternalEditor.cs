//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Resolves and describes the external editor used by commands such as
/// <c>theme edit</c> and <c>edit</c>.
/// </summary>
internal static class ExternalEditor
{
    /// <summary>
    /// Resolves the external editor to launch. Lookup order: explicit
    /// <paramref name="explicitEditor"/> argument, <c>$VISUAL</c>, <c>$EDITOR</c>,
    /// then a platform default (<c>notepad</c> on Windows, <c>nano</c> on Unix).
    /// The chosen editor must accept the file path as a positional argument.
    /// </summary>
    /// <param name="explicitEditor">An editor command supplied on the command line, if any.</param>
    /// <returns>The resolved editor invocation, or <c>null</c> when none could be determined.</returns>
    public static EditorInvocation? Resolve(string? explicitEditor)
    {
        var candidate = explicitEditor;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = Environment.GetEnvironmentVariable("VISUAL");
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = Environment.GetEnvironmentVariable("EDITOR");
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = OperatingSystem.IsWindows() ? "notepad" : "nano";
        }

        return EditorInvocation.Parse(candidate);
    }

    /// <summary>
    /// Parses an editor invocation string (which may include arguments like
    /// <c>code --wait</c>) into a file name plus any prefix arguments.
    /// </summary>
    public sealed record EditorInvocation(string FileName, string? PrefixArgs)
    {
        public string DisplayName => string.IsNullOrEmpty(this.PrefixArgs) ? this.FileName : $"{this.FileName} {this.PrefixArgs}";

        public string BuildArguments(string path)
        {
            var quoted = path.Contains(' ') ? $"\"{path}\"" : path;
            return string.IsNullOrEmpty(this.PrefixArgs) ? quoted : $"{this.PrefixArgs} {quoted}";
        }

        public static EditorInvocation? Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            raw = raw.Trim();

            // Honor an explicitly-quoted executable path: "C:\\Program Files\\X\\editor.exe" --wait
            if (raw.StartsWith('"'))
            {
                var endQuote = raw.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    var file = raw.Substring(1, endQuote - 1);
                    var rest = raw[(endQuote + 1)..].Trim();
                    return new EditorInvocation(file, string.IsNullOrEmpty(rest) ? null : rest);
                }
            }

            var firstSpace = raw.IndexOf(' ');
            if (firstSpace < 0)
            {
                return new EditorInvocation(raw, null);
            }

            return new EditorInvocation(raw[..firstSpace], raw[(firstSpace + 1)..].Trim());
        }
    }
}
