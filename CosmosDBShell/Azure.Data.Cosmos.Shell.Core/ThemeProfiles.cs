// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System.Collections.Generic;

/// <summary>
/// Built-in <see cref="ThemeOptions"/> profiles. Selected at startup with
/// <c>--theme</c> or the <c>COSMOSDB_SHELL_THEME</c> environment variable, and at
/// runtime with the <c>theme</c> command.
/// </summary>
internal static class ThemeProfiles
{
    /// <summary>
    /// Default palette tuned for dark terminal backgrounds. Uses ANSI 16 colors only,
    /// so it follows the terminal's configured palette (Solarized, Dracula, Campbell,
    /// etc.). Headers and table values are bold/unstyled rather than colored so they
    /// stay readable on light backgrounds too. Literal colors are split per type
    /// (string/number/boolean/null) to approximate VS Code's default dark theme.
    /// </summary>
    public static ThemeOptions Default { get; } = new()
    {
        StringColor = "aqua",
        NumberColor = "lime",
        BooleanColor = "blue",
        NullColor = "blue",
        StringEscapeColor = "olive",
        VariableColor = "teal",
    };

    /// <summary>
    /// Palette tuned for light terminal backgrounds, matching VS Code's light theme JSON
    /// coloring: green keys, blue strings, green numbers, blue <c>true</c>/<c>false</c>/<c>null</c>,
    /// and red string escapes. Bracket cycle uses blue/green/purple so pairs stay visible on white.
    /// </summary>
    public static ThemeOptions Light { get; } = Default with
    {
        BracketCycle = ["blue", "green", "purple"],
        LiteralColor = "purple",
        StringColor = "blue",
        NumberColor = "green",
        BooleanColor = "blue",
        NullColor = "blue",
        StringEscapeColor = "red",
        ContainerNameColor = "purple",
        ConnectedPromptColor = "navy",
        JsonPropertyColor = "green",
        JsonPathColor = "navy",
        VariableColor = "navy",
        HelpAccentColor = "navy",
        DirectoryColor = "navy",
        OperatorColor = "navy",
    };

    /// <summary>
    /// Same defaults as <see cref="Default"/>; provided as an explicit profile so
    /// users can opt in by name. Constructed with <c>Default with { }</c> so it
    /// is a distinct instance from <see cref="Default"/> — this is required for
    /// <see cref="ThemeCommand"/>'s reference-equality lookup of the active name.
    /// </summary>
    public static ThemeOptions Dark { get; } = Default with { };

    /// <summary>
    /// No colors anywhere — only <c>[bold]</c>/<c>[dim]</c>/<c>[underline]</c>
    /// modifiers. Useful for screen readers, monochrome terminals, or piping to a
    /// log file where escape sequences would clutter the output.
    /// </summary>
    public static ThemeOptions Monochrome { get; } = new()
    {
        CommandColor = string.Empty,
        UnknownCommandStyle = "bold",
        ArgumentNameColor = string.Empty,
        ConnectedPromptColor = string.Empty,
        DatabaseNameColor = string.Empty,
        ContainerNameColor = string.Empty,
        RedirectionColor = string.Empty,
        JsonPropertyColor = string.Empty,
        JsonPunctuationColor = string.Empty,
        LiteralColor = string.Empty,
        StringColor = string.Empty,
        NumberColor = string.Empty,
        BooleanColor = string.Empty,
        NullColor = string.Empty,
        StringEscapeColor = string.Empty,
        VariableColor = string.Empty,
        JsonPathColor = string.Empty,
        KeywordColor = string.Empty,
        ErrorColor = string.Empty,
        OperatorColor = string.Empty,
        TableValueColor = string.Empty,
        WarningColor = string.Empty,
        DirectoryColor = string.Empty,
        MutedColor = "dim",
        HelpAccentColor = string.Empty,
        HelpPlaceholderColor = string.Empty,
        HelpVariableColor = string.Empty,
        HelpBorderColor = string.Empty,
        HelpHeaderStyle = "bold",
        HelpNameStyle = "bold",
        BracketCycle = [string.Empty],
    };

    /// <summary>
    /// All profiles by name. Lookup is case-insensitive.
    /// </summary>
    public static IReadOnlyDictionary<string, ThemeOptions> All { get; } = new Dictionary<string, ThemeOptions>(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = Default,
        ["light"] = Light,
        ["dark"] = Dark,
        ["monochrome"] = Monochrome,
    };

    /// <summary>
    /// Resolves a theme by name. Returns <c>true</c> on success; on unknown name returns
    /// <c>false</c> and emits the <see cref="Default"/> profile via <paramref name="profile"/>.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="ThemeRegistry.Instance"/> so file-loaded themes are
    /// found alongside the built-ins.
    /// </remarks>
    public static bool TryGet(string? name, out ThemeOptions profile)
    {
        return ThemeRegistry.Instance.TryGet(name, out profile);
    }
}
