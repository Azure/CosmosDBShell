// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Picks the closest known command name for a user typo using Levenshtein
/// distance, so the shell can surface a "did you mean…?" hint.
/// </summary>
internal static class CommandNameSuggester
{
    /// <summary>
    /// Returns the candidate closest to <paramref name="typed"/> within an
    /// edit-distance budget, or null when no candidate is close enough.
    /// </summary>
    public static string? Suggest(string typed, IEnumerable<string> candidates)
    {
        if (string.IsNullOrEmpty(typed) || candidates == null)
        {
            return null;
        }

        // Budget scales with the typed length so short typos still match
        // (e.g. "ls" -> "ls" needs distance 0; "lss" -> "ls" needs 1) while
        // long inputs tolerate a couple of edits without matching random
        // unrelated commands.
        var budget = Math.Max(1, (typed.Length / 3) + 1);

        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (string.Equals(candidate, typed, StringComparison.Ordinal))
            {
                // Exact match would have been resolved by the caller; ignore.
                continue;
            }

            var distance = LevenshteinDistance(typed, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return bestDistance <= budget ? best : null;
    }

    private static int LevenshteinDistance(string source, string target)
    {
        var s = source.ToLowerInvariant();
        var t = target.ToLowerInvariant();
        if (s.Length == 0)
        {
            return t.Length;
        }

        if (t.Length == 0)
        {
            return s.Length;
        }

        var previous = new int[t.Length + 1];
        var current = new int[t.Length + 1];

        for (var j = 0; j <= t.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= s.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[t.Length];
    }
}
