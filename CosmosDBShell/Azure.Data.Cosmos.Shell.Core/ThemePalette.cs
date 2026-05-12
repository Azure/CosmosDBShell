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
}
