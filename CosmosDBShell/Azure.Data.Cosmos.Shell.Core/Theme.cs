// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using Spectre.Console;

/// <summary>
/// Provides formatting utilities for shell themes and prompts.
/// </summary>
/// <remarks>
/// All colors used here are the standard ANSI 16 color names (<c>black</c>, <c>maroon</c>,
/// <c>green</c>, <c>olive</c>, <c>navy</c>, <c>purple</c>, <c>teal</c>, <c>silver</c>,
/// <c>grey</c>, <c>red</c>, <c>lime</c>, <c>yellow</c>, <c>blue</c>, <c>fuchsia</c>,
/// <c>aqua</c>, <c>white</c>). These map to the terminal's configured 16-color palette,
/// so the shell's appearance follows the user's terminal theme. Spectre's <c>cyan</c>
/// and <c>magenta</c> are <em>not</em> in this set — they resolve to fixed 256-color
/// indices and bypass terminal theming, so we use <c>aqua</c>/<c>fuchsia</c> instead.
/// </remarks>
internal static class Theme
{
    /// <summary>Color used for known shell command names and script paths.</summary>
    public const string CommandColorName = "yellow";

    /// <summary>Color used for unknown command names (always rendered bold).</summary>
    public const string UnknownCommandColorName = "red";

    /// <summary>Color used for known argument/option names.</summary>
    public const string ArgumentNameColorName = "green";

    /// <summary>Color used for the connected prompt and JSON property names.</summary>
    public const string ConnectedPromptColorName = "aqua";

    /// <summary>Color used for database names in the prompt and listings.</summary>
    public const string DatabaseNameColorName = "green";

    /// <summary>Color used for container names in the prompt and listings.</summary>
    public const string ContainerNameColorName = "fuchsia";

    /// <summary>Color used for output redirection operators.</summary>
    public const string RedirectionColorName = "green";

    /// <summary>Color used for JSON property names in syntax-highlighted output.</summary>
    public const string JsonPropertyColorName = "aqua";

    /// <summary>Color used for JSON punctuation (comma, colon).</summary>
    public const string JsonPunctuationColorName = "yellow";

    /// <summary>Color used for JSON and shell string/number/boolean/null literals.</summary>
    public const string LiteralColorName = "fuchsia";

    /// <summary>Color used for shell keywords (e.g. <c>if</c>, <c>while</c>).</summary>
    public const string KeywordColorName = "purple";

    /// <summary>Color used for error markers.</summary>
    public const string ErrorColorName = "red";

    /// <summary>Color used for operators in shell expressions.</summary>
    public const string OperatorColorName = "blue";

    /// <summary>Color used for table header values rendered in plain reports.</summary>
    public const string TableValueColorName = "white";

    /// <summary>Color used for warning/notice text (e.g. throughput unavailable).</summary>
    public const string WarningColorName = "yellow";

    /// <summary>Color used for directory entries.</summary>
    public const string DirectoryColorName = "blue";

    /// <summary>Color used for muted/secondary metadata (timestamps, indices).</summary>
    public const string MutedColorName = "grey";

    /// <summary>Color used for help bullet markers and similar accents.</summary>
    public const string HelpAccentColorName = "aqua";

    /// <summary>Color used for help section text and ordinals.</summary>
    public const string HelpSecondaryColorName = "silver";

    /// <summary>Color used for placeholder syntax (<c>&lt;name&gt;</c>) in help text.</summary>
    public const string HelpPlaceholderColorName = "yellow";

    /// <summary>Color used for variable sigils in help text.</summary>
    public const string HelpVariableColorName = "green";

    /// <summary>Color used for help section rule titles.</summary>
    public const string HelpRuleColorName = "yellow";

    /// <summary>Pre-formatted markup open tag for known commands.</summary>
    public const string CommandColor = "[" + CommandColorName + "]";

    /// <summary>
    /// Colors used for paired brackets ({}, [], ()) cycled by nesting depth, similar to
    /// the "bracket pair colorization" feature in modern editors. The cycle is shared
    /// across bracket types so that a single visual depth counter spans every kind of
    /// pair. All entries are ANSI-16 colors so the cycle follows the terminal theme.
    /// </summary>
    private static readonly string[] BracketDepthColors =
    {
        "yellow",
        "fuchsia",
        "aqua",
    };

    public static string FormatUnknownCommand(string command)
    {
        return $"[bold {UnknownCommandColorName}]{Markup.Escape(command)}[/]";
    }

    public static string FormatCommand(string command)
    {
        return $"{CommandColor}{Markup.Escape(command)}[/]";
    }

    public static string FormatScriptPath(string command)
    {
        return $"{Theme.CommandColor}{Markup.Escape(command)}[/]";
    }

    public static string FormatArgumentName(string command)
    {
        return $"[{ArgumentNameColorName}]{Markup.Escape(command)}[/]";
    }

    public static string ConnectedStatePromt(string prompt)
    {
        return $"[{ConnectedPromptColorName}]{Markup.Escape(prompt)}[/]";
    }

    public static string DisconnectedStatePromt(string prompt)
    {
        return prompt;
    }

    public static string DatabaseNamePromt(string db)
    {
        return $"[{DatabaseNameColorName}]{Markup.Escape(db)}[/]";
    }

    public static string ContainerNamePromt(string cn)
    {
        return $"[{ContainerNameColorName}]{Markup.Escape(cn)}[/]";
    }

    public static string FormatRedirection(string v)
    {
        return $"[{RedirectionColorName}]{Markup.Escape(v)}[/]";
    }

    public static string FormatRedirectionDestination(string v)
    {
        return $"[{RedirectionColorName} underline]{Markup.Escape(v)}[/]";
    }

    public static string FormatJsonProperty(string text)
    {
        return $"[{JsonPropertyColorName}]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonBracket(string text)
    {
        return $"[{JsonPunctuationColorName}]{Markup.Escape(text)}[/]";
    }

    /// <summary>
    /// Returns the Spectre.Console color name for a bracket at the given (zero-based)
    /// nesting depth. Colors cycle when the depth exceeds the palette length.
    /// </summary>
    public static string GetBracketColor(int depth)
    {
        if (depth < 0)
        {
            depth = 0;
        }

        return BracketDepthColors[depth % BracketDepthColors.Length];
    }

    /// <summary>
    /// Formats a single bracket character ('{', '}', '[', ']', '(', ')') with the
    /// depth-cycled color. Comma and colon should continue to use
    /// <see cref="FormatJsonBracket"/> instead.
    /// </summary>
    public static string FormatBracket(string text, int depth)
    {
        return $"[{GetBracketColor(depth)}]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonString(string text)
    {
        return $"[{LiteralColorName}]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonNumber(string text)
    {
        return $"[{LiteralColorName}]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonBoolean(string text)
    {
        return $"[{LiteralColorName}]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonNull(string text)
    {
        return $"[{LiteralColorName}]{Markup.Escape(text)}[/]";
    }

    internal static string FormatStringLiteral(string text)
    {
        return $"[{LiteralColorName}]{Markup.Escape(text)}[/]";
    }

    internal static string FormatNumberLiteral(string v)
    {
        return FormatStringLiteral(v);
    }

    internal static string FormatBooleanLiteral(string v)
    {
        return FormatStringLiteral(v);
    }

    internal static string FormatKeyword(string value)
    {
        return $"[{KeywordColorName}]{Markup.Escape(value)}[/]";
    }

    internal static string FormatError(string value)
    {
        return $"[{ErrorColorName}]{Markup.Escape(value)}[/]";
    }

    internal static string FormatOperator(string value)
    {
        return $"[{OperatorColorName}]{Markup.Escape(value)}[/]";
    }

    /// <summary>
    /// Wraps a value in the standard table-cell color used by report-style tables
    /// (e.g. <c>connect</c>, <c>settings</c>, <c>query</c> metrics).
    /// </summary>
    internal static string FormatTableValue(string value)
    {
        return $"[{TableValueColorName}]{Markup.Escape(value)}[/]";
    }

    /// <summary>
    /// Wraps already-formatted markup in the table-value color without re-escaping.
    /// Use when the inner string is a numeric/literal value that contains no Spectre
    /// markup characters (or has already been escaped by the caller).
    /// </summary>
    internal static string FormatTableValueRaw(string markup)
    {
        return $"[{TableValueColorName}]{markup}[/]";
    }

    /// <summary>Wraps text in the warning/notice color.</summary>
    internal static string FormatWarning(string value)
    {
        return $"[{WarningColorName}]{Markup.Escape(value)}[/]";
    }

    /// <summary>Wraps text in the directory color (used by <c>dir</c> output).</summary>
    internal static string FormatDirectory(string value)
    {
        return $"[{DirectoryColorName}]{Markup.Escape(value)}[/]";
    }

    /// <summary>Wraps text in the muted color (used for timestamps and other metadata).</summary>
    internal static string FormatMuted(string value)
    {
        return $"[{MutedColorName}]{Markup.Escape(value)}[/]";
    }

    /// <summary>
    /// Wraps a help section/category header in <c>[bold]</c> with no foreground color.
    /// Bright/light foreground colors (aqua, cyan, silver, white) become unreadable on
    /// light terminal backgrounds, so we let the terminal's default foreground handle
    /// contrast in both light and dark themes.
    /// </summary>
    internal static string FormatHelpHeader(string value)
    {
        return $"[bold]{Markup.Escape(value)}[/]";
    }

    /// <summary>
    /// Wraps help body text (descriptions, command summaries) in unstyled escaped text so
    /// the terminal's default foreground is used. See <see cref="FormatHelpHeader"/> for
    /// rationale.
    /// </summary>
    internal static string FormatHelpDescription(string value)
    {
        return Markup.Escape(value);
    }

    /// <summary>
    /// Wraps a help-line name token (parameter, option, or syntax element) in
    /// <c>[bold]</c> so it stands out against descriptions while remaining readable on
    /// any terminal background.
    /// </summary>
    internal static string FormatHelpName(string value)
    {
        return $"[bold]{Markup.Escape(value)}[/]";
    }
}
