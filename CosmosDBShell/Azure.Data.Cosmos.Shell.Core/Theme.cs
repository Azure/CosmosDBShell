// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using Spectre.Console;

/// <summary>
/// Provides formatting utilities for shell themes and prompts.
/// </summary>
internal static class Theme
{
    public const string CommandColor = "[lightyellow3]";

    /// <summary>
    /// Colors used for paired brackets ({}, [], ()) cycled by nesting depth, similar to
    /// the "bracket pair colorization" feature in modern editors. The cycle is shared
    /// across bracket types so that a single visual depth counter spans every kind of
    /// pair.
    /// </summary>
    private static readonly string[] BracketDepthColors =
    {
        "gold1",
        "orchid",
        "deepskyblue1",
    };

    public static string FormatUnknownCommand(string command)
    {
        return $"[bold red]{Markup.Escape(command)}[/]";
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
        return $"[green]{Markup.Escape(command)}[/]";
    }

    public static string ConnectedStatePromt(string prompt)
    {
        return $"[cyan]{Markup.Escape(prompt)}[/]";
    }

    public static string DisconnectedStatePromt(string prompt)
    {
        return prompt;
    }

    public static string DatabaseNamePromt(string db)
    {
        return $"[green]{Markup.Escape(db)}[/]";
    }

    public static string ContainerNamePromt(string cn)
    {
        return $"[magenta]{Markup.Escape(cn)}[/]";
    }

    public static string FormatRedirection(string v)
    {
        return $"[seagreen2]{Markup.Escape(v)}[/]";
    }

    public static string FormatRedirectionDestination(string v)
    {
        return $"[seagreen2 underline]{Markup.Escape(v)}[/]";
    }

    public static string FormatJsonProperty(string text)
    {
        return $"[cyan]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonBracket(string text)
    {
        return $"[yellow]{Markup.Escape(text)}[/]";
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
        return $"[violet]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonNumber(string text)
    {
        return $"[violet]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonBoolean(string text)
    {
        return $"[violet]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonNull(string text)
    {
        return $"[violet]{Markup.Escape(text)}[/]";
    }

    internal static string FormatStringLiteral(string text)
    {
        return $"[violet]{Markup.Escape(text)}[/]";
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
        return $"[plum3]{Markup.Escape(value)}[/]";
    }

    internal static string FormatError(string value)
    {
        return $"[red]{Markup.Escape(value)}[/]";
    }

    internal static string FormatOperator(string value)
    {
        return $"[deepskyblue1]{Markup.Escape(value)}[/]";
    }
}
