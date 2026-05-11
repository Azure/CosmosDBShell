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

    public string JsonPropertyColor { get; init; } = "aqua";

    public string JsonPunctuationColor { get; init; } = "yellow";

    public string LiteralColor { get; init; } = "fuchsia";

    public string KeywordColor { get; init; } = "purple";

    public string ErrorColor { get; init; } = "red";

    public string OperatorColor { get; init; } = "blue";

    public string TableValueColor { get; init; } = "white";

    public string WarningColor { get; init; } = "yellow";

    public string DirectoryColor { get; init; } = "blue";

    public string MutedColor { get; init; } = "grey";

    public string HelpAccentColor { get; init; } = "aqua";

    public string HelpPlaceholderColor { get; init; } = "yellow";

    public string HelpVariableColor { get; init; } = "green";

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
    public string[] BracketCycle { get; init; } = ["yellow", "fuchsia", "aqua"];
}
