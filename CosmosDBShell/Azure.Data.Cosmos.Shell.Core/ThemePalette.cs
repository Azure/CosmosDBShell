// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System.Collections.Generic;

/// <summary>
/// Allowed tokens for theme color/style values. Built-in profiles, file-loaded
/// profiles, and validation tests all reference this single source of truth so a
/// new ANSI 16 color, modifier, or hex format only needs to be added in one place.
/// </summary>
internal static class ThemePalette
{
    /// <summary>The standard ANSI 16 color names supported by Spectre.Console.</summary>
    public static readonly IReadOnlyList<string> AnsiSixteen = new[]
    {
        "black", "maroon", "green", "olive", "navy", "purple", "teal", "silver",
        "grey", "red", "lime", "yellow", "blue", "fuchsia", "aqua", "white",
    };

    /// <summary>Spectre.Console style modifiers that may be combined with a color.</summary>
    public static readonly IReadOnlyList<string> Modifiers = new[]
    {
        "bold", "dim", "italic", "underline", "strikethrough", "invert", "conceal",
        "slowblink", "rapidblink",
    };

    private static readonly HashSet<string> AnsiSixteenSet = new(AnsiSixteen, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ModifiersSet = new(Modifiers, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="token"/> is an allowed ANSI 16 color name
    /// or a Spectre style modifier.
    /// </summary>
    public static bool IsAllowedToken(string token)
    {
        return !string.IsNullOrEmpty(token)
            && (AnsiSixteenSet.Contains(token) || ModifiersSet.Contains(token));
    }

    /// <summary>Returns <c>true</c> when <paramref name="token"/> is an ANSI 16 color.</summary>
    public static bool IsAnsiSixteen(string token)
    {
        return !string.IsNullOrEmpty(token) && AnsiSixteenSet.Contains(token);
    }

    /// <summary>Returns <c>true</c> when <paramref name="token"/> is an allowed style modifier.</summary>
    public static bool IsModifier(string token)
    {
        return !string.IsNullOrEmpty(token) && ModifiersSet.Contains(token);
    }

    /// <summary>
    /// Returns the closest valid ANSI 16 color name to <paramref name="token"/>, or
    /// <c>null</c> when no candidate is similar enough to be useful.
    /// </summary>
    public static string? SuggestColor(string token)
    {
        return SuggestNearest(token, AnsiSixteen);
    }

    /// <summary>
    /// Returns the closest valid ANSI 16 color name or modifier to <paramref name="token"/>,
    /// or <c>null</c> when no candidate is similar enough to be useful.
    /// </summary>
    public static string? SuggestColorOrModifier(string token)
    {
        return SuggestNearest(token, AnsiSixteen.Concat(Modifiers));
    }

    private static string? SuggestNearest(string token, IEnumerable<string> candidates)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var lower = token.ToLowerInvariant();
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = LevenshteinDistance(lower, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        var maxAcceptable = Math.Max(1, token.Length / 2);
        return bestDistance <= maxAcceptable ? best : null;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        if (s.Length == 0)
        {
            return t.Length;
        }

        if (t.Length == 0)
        {
            return s.Length;
        }

        var prev = new int[t.Length + 1];
        var curr = new int[t.Length + 1];
        for (var j = 0; j <= t.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= s.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[t.Length];
    }
}
