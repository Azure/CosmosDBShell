// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using System.Collections.Generic;

/// <summary>
/// Builds the user-facing "Unknown option 'X'." message, optionally
/// followed by a Levenshtein-based "Did you mean 'Y'?" hint, in a
/// consistent shape across CommandStatement and CommandExpression
/// option-binding paths.
/// </summary>
internal static class UnknownOptionMessage
{
    /// <summary>
    /// Returns a localized "Unknown option '<paramref name="prefix"/><paramref name="typedName"/>'."
    /// message with an optional suggestion drawn from
    /// <paramref name="knownNames"/>. The same <paramref name="prefix"/>
    /// (e.g. "-" or "--") is reused in the suggestion so the user can
    /// copy-paste it back as-is.
    /// </summary>
    public static string Build(string prefix, string typedName, IEnumerable<string> knownNames)
    {
        var displayed = prefix + typedName;
        var baseMsg = MessageService.GetString(
            "error-unknown-option",
            new Dictionary<string, object> { { "option", displayed } })
            ?? $"Unknown option '{displayed}'.";

        var suggestion = CommandNameSuggester.Suggest(typedName, knownNames);
        if (string.IsNullOrEmpty(suggestion))
        {
            return baseMsg;
        }

        var hint = MessageService.GetString(
            "error-unknown-option-suggestion",
            new Dictionary<string, object> { { "suggestion", prefix + suggestion } })
            ?? $"Did you mean '{prefix}{suggestion}'?";

        return string.IsNullOrEmpty(hint) ? baseMsg : baseMsg + " " + hint;
    }
}
