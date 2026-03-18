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

    public static string FormatJsonProperty(string text)
    {
        return $"[cyan]{Markup.Escape(text)}[/]";
    }

    public static string FormatJsonBracket(string text)
    {
        return $"[yellow]{Markup.Escape(text)}[/]";
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
}
