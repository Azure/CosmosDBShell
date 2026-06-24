// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// A snapshot of every color slot used by the shell. <see cref="Theme"/> reads from
/// <see cref="Theme.Current"/> when emitting markup, so swapping <c>Current</c> for a
/// different <see cref="ThemeOptions"/> instance reskins the entire shell at runtime.
/// </summary>
/// <remarks>
/// <para>Each property holds a Spectre.Console color name (or empty for "use the
/// terminal's default foreground"). Bold/italic/underline modifiers belong on the
/// <see cref="HelpHeaderStyle"/>, <see cref="HelpNameStyle"/>, and
/// <see cref="UnknownCommandStyle"/> string slots — these are passed verbatim into
/// the markup tag.</para>
/// <para>Profiles are defined in <see cref="ThemeProfiles"/>. To add a new
/// per-role color, add the property here, the corresponding default in
/// <see cref="ThemeProfiles.Default"/>, and update the <see cref="Theme"/> helper
/// that consumes it.</para>
/// </remarks>
internal sealed record ThemeOptions
{
    public string CommandColor { get; init; } = "yellow";

    public string UnknownCommandStyle { get; init; } = "bold red";

    public string ArgumentNameColor { get; init; } = "green";

    public string ConnectedPromptColor { get; init; } = "aqua";

    public string DatabaseNameColor { get; init; } = "green";

    public string ContainerNameColor { get; init; } = "fuchsia";

    public string RedirectionColor { get; init; } = "green";

    public string JsonPropertyColor { get; init; } = "green";

    public string JsonPunctuationColor { get; init; } = "yellow";

    public string LiteralColor { get; init; } = "aqua";

    /// <summary>
    /// Gets the color for string literals. Empty falls back to <see cref="LiteralColor"/>,
    /// so themes that only set <c>literal</c> keep coloring every literal the same.
    /// </summary>
    public string StringColor { get; init; } = string.Empty;

    /// <summary>
    /// Gets the color for numeric literals. Empty falls back to <see cref="LiteralColor"/>.
    /// </summary>
    public string NumberColor { get; init; } = string.Empty;

    /// <summary>
    /// Gets the color for boolean literals (<c>true</c>/<c>false</c>). Empty falls back to
    /// <see cref="LiteralColor"/>.
    /// </summary>
    public string BooleanColor { get; init; } = string.Empty;

    /// <summary>
    /// Gets the color for the <c>null</c> literal. Empty falls back to <see cref="LiteralColor"/>.
    /// </summary>
    public string NullColor { get; init; } = string.Empty;

    /// <summary>
    /// Gets the color for backslash escape sequences (e.g. <c>\n</c>, <c>\"</c>,
    /// <c>\uXXXX</c>) inside string literals. Empty (or a value equal to the resolved
    /// string color) colors escapes the same as the rest of the string.
    /// </summary>
    public string StringEscapeColor { get; init; } = string.Empty;

    /// <summary>
    /// Gets the color applied to variable references (e.g. <c>$foo</c>) in the
    /// prompt highlighter. Empty string falls back to the terminal default.
    /// </summary>
    public string VariableColor { get; init; } = "aqua";

    /// <summary>
    /// Gets the color applied to JSON path expressions (e.g. <c>.items[0].name</c>)
    /// in the prompt highlighter. Empty string falls back to the terminal default.
    /// </summary>
    public string JsonPathColor { get; init; } = "aqua";

    public string KeywordColor { get; init; } = "purple";

    public string ErrorColor { get; init; } = "red";

    public string OperatorColor { get; init; } = "teal";

    public string TableValueColor { get; init; } = "white";

    public string WarningColor { get; init; } = "yellow";

    public string DirectoryColor { get; init; } = "blue";

    public string MutedColor { get; init; } = "grey";

    public string HelpAccentColor { get; init; } = "aqua";

    public string HelpPlaceholderColor { get; init; } = "yellow";

    public string HelpVariableColor { get; init; } = "green";

    /// <summary>
    /// Color applied to the border of help title panels. Empty string means the
    /// terminal default (no color escape), used by the monochrome profile.
    /// </summary>
    public string HelpBorderColor { get; init; } = "green";

    /// <summary>
    /// Style applied to help section/category headers and statement-help titles.
    /// Empty string means terminal default with no modifiers.
    /// </summary>
    public string HelpHeaderStyle { get; init; } = "bold";

    /// <summary>
    /// Style applied to parameter/option name tokens in help syntax lines.
    /// </summary>
    public string HelpNameStyle { get; init; } = "bold";

    /// <summary>
    /// Colors used for paired brackets ({}, [], ()) cycled by nesting depth. Must be
    /// non-empty.
    /// </summary>
    public string[] BracketCycle { get; init; } = ["yellow", "fuchsia", "blue"];
}
