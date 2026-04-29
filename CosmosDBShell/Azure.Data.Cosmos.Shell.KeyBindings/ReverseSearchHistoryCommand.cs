//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using System;
using System.Collections.Generic;
using System.IO;
using Azure.Data.Cosmos.Shell.Core;
using RadLine;
using Spectre.Console;

internal class ReverseSearchHistoryCommand(ShellInterpreter shell, bool startsForward = false) : LineEditorCommand
{
    private readonly ShellInterpreter shell = shell;
    private readonly bool startsForward = startsForward;

    public override void Execute(LineEditorContext context)
    {
        var originalContent = context.Buffer.Content;
        var originalPosition = context.Buffer.Position;
        var query = string.Empty;
        var result = ReverseHistorySearchResult.None();
        var accepted = false;
        var isForwardSearch = this.startsForward;
        var promptRow = TryGetCursorTop();
        var restoreControlCHandling = TrySetTreatControlCAsInput(true, out var originalTreatControlCAsInput);

        try
        {
            Render(query, result, isForwardSearch, promptRow, this.shell);

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    accepted = result.HasMatch;
                    break;
                }

                if (key.Key == ConsoleKey.Escape ||
                    (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.G) ||
                    (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C))
                {
                    break;
                }

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.R)
                {
                    isForwardSearch = false;
                    if (result.HasMatch)
                    {
                        result = ReverseHistorySearch.FindNextMatch(this.shell.History, query, result.Skip);
                    }

                    Render(query, result, isForwardSearch, promptRow, this.shell);
                    continue;
                }

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.S)
                {
                    isForwardSearch = true;
                    if (result.HasMatch)
                    {
                        result = ReverseHistorySearch.FindPreviousMatch(this.shell.History, query, result.Skip);
                    }

                    Render(query, result, isForwardSearch, promptRow, this.shell);
                    continue;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (query.Length > 0)
                    {
                        query = query[..^1];
                        result = FindInitialMatch(this.shell.History, query, isForwardSearch);
                    }

                    Render(query, result, isForwardSearch, promptRow, this.shell);
                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    query += key.KeyChar;
                    result = FindInitialMatch(this.shell.History, query, isForwardSearch);
                    Render(query, result, isForwardSearch, promptRow, this.shell);
                    continue;
                }
            }
        }
        finally
        {
            if (restoreControlCHandling)
            {
                TrySetTreatControlCAsInput(originalTreatControlCAsInput, out _);
            }

            ClearLine(promptRow);
        }

        var newContent = accepted ? result.Match : originalContent;
        context.Buffer.Clear(0, context.Buffer.Length);
        context.Buffer.Move(0);
        if (newContent.Length > 0)
        {
            context.Buffer.Insert(newContent);
        }

        context.Buffer.Move(accepted ? newContent.Length : Math.Min(originalPosition, newContent.Length));
    }

    private static ReverseHistorySearchResult FindInitialMatch(IReadOnlyList<string> history, string query, bool isForwardSearch)
    {
        return isForwardSearch ? ReverseHistorySearch.FindInitialForwardMatch(history, query) : ReverseHistorySearch.FindInitialMatch(history, query);
    }

    private static void Render(string query, ReverseHistorySearchResult result, bool isForwardSearch, int? promptRow, ShellInterpreter shell)
    {
        ClearLine(promptRow);
        AnsiConsole.Markup(ReverseHistorySearch.FormatSearchPromptMarkup(query, result.Match, result.HasMatch, isForwardSearch, shell, TryGetLineWidth()));
    }

    private static void ClearLine(int? promptRow)
    {
        try
        {
            var width = TryGetLineWidth();
            if (!width.HasValue || width.Value <= 0)
            {
                Console.Write('\r');
                return;
            }

            var row = promptRow ?? Console.CursorTop;
            Console.SetCursorPosition(0, row);
            Console.Write(new string(' ', width.Value));
            Console.SetCursorPosition(0, row);
        }
        catch (IOException)
        {
            Console.Write('\r');
        }
        catch (ArgumentOutOfRangeException)
        {
            Console.Write('\r');
        }
        catch (InvalidOperationException)
        {
            Console.Write('\r');
        }
    }

    private static int? TryGetCursorTop()
    {
        try
        {
            return Console.CursorTop;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int? TryGetLineWidth()
    {
        try
        {
            var width = Console.WindowWidth > 0 ? Console.WindowWidth : Console.BufferWidth;
            return width > 1 ? width - 1 : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TrySetTreatControlCAsInput(bool value, out bool originalValue)
    {
        try
        {
            originalValue = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = value;
            return true;
        }
        catch (IOException)
        {
            originalValue = false;
            return false;
        }
        catch (InvalidOperationException)
        {
            originalValue = false;
            return false;
        }
    }
}
