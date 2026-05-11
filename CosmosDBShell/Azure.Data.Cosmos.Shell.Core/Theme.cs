// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using Spectre.Console;

/// <summary>
/// Provides formatting utilities for shell themes and prompts.
/// </summary>
/// <remarks>
/// <para>All colors are read from <see cref="Current"/>, a swappable
/// <see cref="ThemeOptions"/> instance. Replacing it with <see cref="Apply"/>
/// reskins the entire shell at runtime. Defaults come from
/// <see cref="ThemeProfiles.Default"/>.</para>
/// <para>Profiles use the standard ANSI 16 color names (<c>black</c>, <c>maroon</c>,
/// <c>green</c>, <c>olive</c>, <c>navy</c>, <c>purple</c>, <c>teal</c>, <c>silver</c>,
/// <c>grey</c>, <c>red</c>, <c>lime</c>, <c>yellow</c>, <c>blue</c>, <c>fuchsia</c>,
/// <c>aqua</c>, <c>white</c>) so the shell follows the terminal's configured
/// 16-color palette. Spectre's <c>cyan</c> and <c>magenta</c> are <em>not</em> in
/// this set — they resolve to fixed 256-color indices and bypass terminal theming,
/// so we use <c>aqua</c>/<c>fuchsia</c> instead.</para>
/// </remarks>
internal static class Theme
{
    /// <summary>
    /// Gets the active theme. Mutated by <see cref="Apply"/>; defaults to
    /// <see cref="ThemeProfiles.Default"/>.
    /// </summary>
    public static ThemeOptions Current { get; private set; } = ThemeProfiles.Default;

    // Backward-compatible color-name aliases so existing call sites that interpolate
    // [Theme.XColorName]...[/] keep working. New code should call the Format* helpers
    // instead so they automatically pick up theme changes.
    public static string CommandColorName => Current.CommandColor;

    public static string UnknownCommandColorName => Current.UnknownCommandStyle;

    public static string ArgumentNameColorName => Current.ArgumentNameColor;

    public static string ConnectedPromptColorName => Current.ConnectedPromptColor;

    public static string DatabaseNameColorName => Current.DatabaseNameColor;

    public static string ContainerNameColorName => Current.ContainerNameColor;

    public static string RedirectionColorName => Current.RedirectionColor;

    public static string JsonPropertyColorName => Current.JsonPropertyColor;

    public static string JsonPunctuationColorName => Current.JsonPunctuationColor;

    public static string LiteralColorName => Current.LiteralColor;

    public static string KeywordColorName => Current.KeywordColor;

    public static string ErrorColorName => Current.ErrorColor;

    public static string OperatorColorName => Current.OperatorColor;

    public static string TableValueColorName => Current.TableValueColor;

    public static string WarningColorName => Current.WarningColor;

    public static string DirectoryColorName => Current.DirectoryColor;

    public static string MutedColorName => Current.MutedColor;

    public static string HelpAccentColorName => Current.HelpAccentColor;

    public static string HelpPlaceholderColorName => Current.HelpPlaceholderColor;

    public static string HelpVariableColorName => Current.HelpVariableColor;

    /// <summary>Gets the pre-formatted markup open tag for known commands.</summary>
    public static string CommandColor => OpenTag(Current.CommandColor);

    /// <summary>
    /// Replaces the active theme. Subsequent calls to any <c>Format*</c> helper or
    /// color-name accessor will use values from <paramref name="options"/>.
    /// </summary>
    public static void Apply(ThemeOptions options)
    {
        Current = options ?? throw new ArgumentNullException(nameof(options));
    }

    public static string FormatUnknownCommand(string command)
    {
        return Wrap(Current.UnknownCommandStyle, Markup.Escape(command));
    }

    public static string FormatCommand(string command)
    {
        return Wrap(Current.CommandColor, Markup.Escape(command));
    }

    public static string FormatScriptPath(string command)
    {
        return Wrap(Current.CommandColor, Markup.Escape(command));
    }

    public static string FormatArgumentName(string command)
    {
        return Wrap(Current.ArgumentNameColor, Markup.Escape(command));
    }

    public static string ConnectedStatePromt(string prompt)
    {
        return Wrap(Current.ConnectedPromptColor, Markup.Escape(prompt));
    }

    public static string DisconnectedStatePromt(string prompt)
    {
        return prompt;
    }

    public static string DatabaseNamePromt(string db)
    {
        return Wrap(Current.DatabaseNameColor, Markup.Escape(db));
    }

    public static string ContainerNamePromt(string cn)
    {
        return Wrap(Current.ContainerNameColor, Markup.Escape(cn));
    }

    public static string FormatRedirection(string v)
    {
        return Wrap(Current.RedirectionColor, Markup.Escape(v));
    }

    public static string FormatRedirectionDestination(string v)
    {
        var style = string.IsNullOrEmpty(Current.RedirectionColor) ? "underline" : Current.RedirectionColor + " underline";
        return Wrap(style, Markup.Escape(v));
    }

    public static string FormatJsonProperty(string text)
    {
        return Wrap(Current.JsonPropertyColor, Markup.Escape(text));
    }

    public static string FormatJsonBracket(string text)
    {
        return Wrap(Current.JsonPunctuationColor, Markup.Escape(text));
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

        var cycle = Current.BracketCycle;
        return cycle[depth % cycle.Length];
    }

    /// <summary>
    /// Formats a single bracket character ('{', '}', '[', ']', '(', ')') with the
    /// depth-cycled color. Comma and colon should continue to use
    /// <see cref="FormatJsonBracket"/> instead.
    /// </summary>
    public static string FormatBracket(string text, int depth)
    {
        return Wrap(GetBracketColor(depth), Markup.Escape(text));
    }

    public static string FormatJsonString(string text)
    {
        return Wrap(Current.LiteralColor, Markup.Escape(text));
    }

    public static string FormatJsonNumber(string text)
    {
        return Wrap(Current.LiteralColor, Markup.Escape(text));
    }

    public static string FormatJsonBoolean(string text)
    {
        return Wrap(Current.LiteralColor, Markup.Escape(text));
    }

    public static string FormatJsonNull(string text)
    {
        return Wrap(Current.LiteralColor, Markup.Escape(text));
    }

    internal static string FormatStringLiteral(string text)
    {
        return Wrap(Current.LiteralColor, Markup.Escape(text));
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
        return Wrap(Current.KeywordColor, Markup.Escape(value));
    }

    internal static string FormatError(string value)
    {
        return Wrap(Current.ErrorColor, Markup.Escape(value));
    }

    internal static string FormatOperator(string value)
    {
        return Wrap(Current.OperatorColor, Markup.Escape(value));
    }

    /// <summary>
    /// Wraps a value in the standard table-cell color used by report-style tables
    /// (e.g. <c>connect</c>, <c>settings</c>, <c>query</c> metrics).
    /// </summary>
    internal static string FormatTableValue(string value)
    {
        return Wrap(Current.TableValueColor, Markup.Escape(value));
    }

    /// <summary>
    /// Wraps already-formatted markup in the table-value color without re-escaping.
    /// Use when the inner string is a numeric/literal value that contains no Spectre
    /// markup characters (or has already been escaped by the caller).
    /// </summary>
    internal static string FormatTableValueRaw(string markup)
    {
        return Wrap(Current.TableValueColor, markup);
    }

    /// <summary>Wraps text in the warning/notice color.</summary>
    internal static string FormatWarning(string value)
    {
        return Wrap(Current.WarningColor, Markup.Escape(value));
    }

    /// <summary>Wraps text in the directory color (used by <c>dir</c> output).</summary>
    internal static string FormatDirectory(string value)
    {
        return Wrap(Current.DirectoryColor, Markup.Escape(value));
    }

    /// <summary>Wraps text in the muted color (used for timestamps and other metadata).</summary>
    internal static string FormatMuted(string value)
    {
        return Wrap(Current.MutedColor, Markup.Escape(value));
    }

    /// <summary>
    /// Wraps a help section/category header using the active
    /// <see cref="ThemeOptions.HelpHeaderStyle"/>. The default profile uses
    /// <c>[bold]</c> with no foreground color so headers fall through to the
    /// terminal's default foreground in both light and dark themes.
    /// </summary>
    internal static string FormatHelpHeader(string value)
    {
        return Wrap(Current.HelpHeaderStyle, Markup.Escape(value));
    }

    /// <summary>
    /// Wraps help body text (descriptions, command summaries) in unstyled escaped
    /// text so the terminal's default foreground is used.
    /// </summary>
    internal static string FormatHelpDescription(string value)
    {
        return Markup.Escape(value);
    }

    /// <summary>
    /// Wraps a help-line name token (parameter, option, or syntax element) using the
    /// active <see cref="ThemeOptions.HelpNameStyle"/>.
    /// </summary>
    internal static string FormatHelpName(string value)
    {
        return Wrap(Current.HelpNameStyle, Markup.Escape(value));
    }

    /// <summary>
    /// Returns <c>[style]content[/]</c> when <paramref name="style"/> is non-empty,
    /// otherwise just <paramref name="content"/>. Centralizing the empty-style branch
    /// here lets profiles disable a color slot entirely (see
    /// <see cref="ThemeProfiles.Monochrome"/>).
    /// </summary>
    private static string Wrap(string style, string content)
    {
        return string.IsNullOrEmpty(style) ? content : $"[{style}]{content}[/]";
    }

    /// <summary>
    /// Returns the open tag <c>[style]</c> for inline markup builders, or empty
    /// string when <paramref name="style"/> is empty. Used by <see cref="CommandColor"/>.
    /// </summary>
    private static string OpenTag(string style)
    {
        return string.IsNullOrEmpty(style) ? string.Empty : $"[{style}]";
    }
}
