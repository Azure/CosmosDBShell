// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.KeyBindings;
using RadLine;
using Spectre.Console;

public class HotkeyCommandTests
{
    [Fact]
    public void MoveToStartOfLine_PlacesCursorAtZero()
    {
        var context = CreateContext("hello world", position: 7);

        new MoveToStartOfLineCommand().Execute(context);

        Assert.Equal(0, context.Buffer.Position);
        Assert.Equal("hello world", context.Buffer.Content);
    }

    [Fact]
    public void MoveToEndOfLine_PlacesCursorAtLength()
    {
        var context = CreateContext("hello world", position: 3);

        new MoveToEndOfLineCommand().Execute(context);

        Assert.Equal(context.Buffer.Length, context.Buffer.Position);
        Assert.Equal("hello world", context.Buffer.Content);
    }

    [Fact]
    public void DeleteToStartOfLine_RemovesTextBeforeCursor()
    {
        var context = CreateContext("hello world", position: 6);

        new DeleteToStartOfLineCommand().Execute(context);

        Assert.Equal("world", context.Buffer.Content);
        Assert.Equal(0, context.Buffer.Position);
    }

    [Fact]
    public void DeleteToStartOfLine_AtBeginning_NoOp()
    {
        var context = CreateContext("hello", position: 0);

        new DeleteToStartOfLineCommand().Execute(context);

        Assert.Equal("hello", context.Buffer.Content);
        Assert.Equal(0, context.Buffer.Position);
    }

    [Fact]
    public void DeleteToEndOfLine_RemovesTextAfterCursor()
    {
        var context = CreateContext("hello world", position: 5);

        new DeleteToEndOfLineCommand().Execute(context);

        Assert.Equal("hello", context.Buffer.Content);
        Assert.Equal(5, context.Buffer.Position);
    }

    [Fact]
    public void DeleteToEndOfLine_AtEnd_NoOp()
    {
        var context = CreateContext("hello", position: 5);

        new DeleteToEndOfLineCommand().Execute(context);

        Assert.Equal("hello", context.Buffer.Content);
        Assert.Equal(5, context.Buffer.Position);
    }

    [Fact]
    public void DeletePreviousWord_RemovesLastWordBeforeCursor()
    {
        var context = CreateContext("hello world", position: 11);

        new DeletePreviousWordCommand().Execute(context);

        Assert.Equal("hello ", context.Buffer.Content);
        Assert.Equal(6, context.Buffer.Position);
    }

    [Fact]
    public void DeletePreviousWord_SkipsTrailingWhitespace()
    {
        var context = CreateContext("hello world   ", position: 14);

        new DeletePreviousWordCommand().Execute(context);

        Assert.Equal("hello ", context.Buffer.Content);
        Assert.Equal(6, context.Buffer.Position);
    }

    [Fact]
    public void DeletePreviousWord_OnlyDeletesUpToCursor()
    {
        var context = CreateContext("hello world today", position: 11);

        new DeletePreviousWordCommand().Execute(context);

        Assert.Equal("hello  today", context.Buffer.Content);
        Assert.Equal(6, context.Buffer.Position);
    }

    [Fact]
    public void DeletePreviousWord_AtBeginning_NoOp()
    {
        var context = CreateContext("hello", position: 0);

        new DeletePreviousWordCommand().Execute(context);

        Assert.Equal("hello", context.Buffer.Content);
        Assert.Equal(0, context.Buffer.Position);
    }

    [Fact]
    public void ExitShell_EmptyBuffer_StopsShell()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.IsRunning = true;
        var context = CreateContext(string.Empty, position: 0);

        new ExitShellCommand(shell).Execute(context);

        Assert.False(shell.IsRunning);
    }

    [Fact]
    public void ExitShell_NonEmptyBuffer_DeletesCharAtCursor()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.IsRunning = true;
        var context = CreateContext("hello", position: 1);

        new ExitShellCommand(shell).Execute(context);

        Assert.True(shell.IsRunning);
        Assert.Equal("hllo", context.Buffer.Content);
        Assert.Equal(1, context.Buffer.Position);
    }

    [Fact]
    public void ExitShell_NonEmptyBufferAtEnd_NoOp()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.IsRunning = true;
        var context = CreateContext("hello", position: 5);

        new ExitShellCommand(shell).Execute(context);

        Assert.True(shell.IsRunning);
        Assert.Equal("hello", context.Buffer.Content);
    }

    [Fact]
    public void MoveCursorLeft_DecrementsPosition()
    {
        var context = CreateContext("hello", position: 3);

        new MoveCursorLeftCommand().Execute(context);

        Assert.Equal(2, context.Buffer.Position);
    }

    [Fact]
    public void MoveCursorLeft_AtBeginning_NoOp()
    {
        var context = CreateContext("hello", position: 0);

        new MoveCursorLeftCommand().Execute(context);

        Assert.Equal(0, context.Buffer.Position);
    }

    [Fact]
    public void MoveCursorRight_IncrementsPosition()
    {
        var context = CreateContext("hello", position: 3);

        new MoveCursorRightCommand().Execute(context);

        Assert.Equal(4, context.Buffer.Position);
    }

    [Fact]
    public void MoveCursorRight_AtEnd_NoOp()
    {
        var context = CreateContext("hello", position: 5);

        new MoveCursorRightCommand().Execute(context);

        Assert.Equal(5, context.Buffer.Position);
    }

    [Fact]
    public void ReverseSearch_FindsNewestSubstringMatch()
    {
        var history = new[] { "ls", "query SELECT * FROM c", "echo hi", "query foo" };

        var index = ReverseHistorySearch.FindReverseMatch(history, "query", 0);

        Assert.Equal(3, index);
    }

    [Fact]
    public void ReverseSearch_SkipsToOlderMatch()
    {
        var history = new[] { "ls", "query SELECT * FROM c", "echo hi", "query foo" };

        var index = ReverseHistorySearch.FindReverseMatch(history, "query", 1);

        Assert.Equal(1, index);
    }

    [Fact]
    public void ReverseSearch_NoMoreMatches_ReturnsMinusOne()
    {
        var history = new[] { "ls", "query foo" };

        var index = ReverseHistorySearch.FindReverseMatch(history, "query", 1);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void ReverseSearch_FindNextReverseMatch_AdvancesToOlderMatch()
    {
        var history = new[] { "ls", "query SELECT * FROM c", "echo hi", "query foo" };

        var result = ReverseHistorySearch.FindNextMatch(history, "query", currentSkip: 0);

        Assert.True(result.HasMatch);
        Assert.Equal(1, result.Index);
        Assert.Equal(1, result.Skip);
        Assert.Equal("query SELECT * FROM c", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindNextReverseMatch_WrapsToNewestMatch()
    {
        var history = new[] { "ls", "query SELECT * FROM c", "echo hi", "query foo" };

        var result = ReverseHistorySearch.FindNextMatch(history, "query", currentSkip: 1);

        Assert.True(result.HasMatch);
        Assert.Equal(3, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal("query foo", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindNextReverseMatch_NoMatches_ReturnsMinusOne()
    {
        var history = new[] { "ls", "echo hi" };

        var result = ReverseHistorySearch.FindNextMatch(history, "query", currentSkip: 0);

        Assert.False(result.HasMatch);
        Assert.Equal(-1, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal(string.Empty, result.Match);
    }

    [Fact]
    public void ReverseSearch_FindInitialMatch_ReturnsNewestMatchResult()
    {
        var history = new[] { "query one", "ls", "query two" };

        var result = ReverseHistorySearch.FindInitialMatch(history, "query");

        Assert.True(result.HasMatch);
        Assert.Equal(2, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal("query two", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindInitialMatch_NoMatch_ReturnsEmptyResult()
    {
        var history = new[] { "ls", "echo hi" };

        var result = ReverseHistorySearch.FindInitialMatch(history, "query");

        Assert.False(result.HasMatch);
        Assert.Equal(-1, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal(string.Empty, result.Match);
    }

    [Fact]
    public void ReverseSearch_FindInitialForwardMatch_ReturnsOldestMatchResult()
    {
        var history = new[] { "query one", "ls", "query two" };

        var result = ReverseHistorySearch.FindInitialForwardMatch(history, "query");

        Assert.True(result.HasMatch);
        Assert.Equal(0, result.Index);
        Assert.Equal(1, result.Skip);
        Assert.Equal("query one", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindInitialForwardMatch_NoMatch_ReturnsEmptyResult()
    {
        var history = new[] { "ls", "echo hi" };

        var result = ReverseHistorySearch.FindInitialForwardMatch(history, "query");

        Assert.False(result.HasMatch);
        Assert.Equal(-1, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal(string.Empty, result.Match);
    }

    [Fact]
    public void ReverseSearch_FindNextMatch_SingleMatch_WrapsToSameResult()
    {
        var history = new[] { "ls", "query foo" };

        var result = ReverseHistorySearch.FindNextMatch(history, "query", currentSkip: 0);

        Assert.True(result.HasMatch);
        Assert.Equal(1, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal("query foo", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindNextMatch_CurrentSkipPastEnd_WrapsToNewestMatch()
    {
        var history = new[] { "query one", "query two" };

        var result = ReverseHistorySearch.FindNextMatch(history, "query", currentSkip: 10);

        Assert.True(result.HasMatch);
        Assert.Equal(1, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal("query two", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindPreviousMatch_AdvancesToNewerMatch()
    {
        var history = new[] { "ls", "query SELECT * FROM c", "echo hi", "query foo" };

        var result = ReverseHistorySearch.FindPreviousMatch(history, "query", currentSkip: 1);

        Assert.True(result.HasMatch);
        Assert.Equal(3, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal("query foo", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindPreviousMatch_WrapsToOldestMatch()
    {
        var history = new[] { "ls", "query SELECT * FROM c", "echo hi", "query foo" };

        var result = ReverseHistorySearch.FindPreviousMatch(history, "query", currentSkip: 0);

        Assert.True(result.HasMatch);
        Assert.Equal(1, result.Index);
        Assert.Equal(1, result.Skip);
        Assert.Equal("query SELECT * FROM c", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindPreviousMatch_SingleMatch_WrapsToSameResult()
    {
        var history = new[] { "ls", "query foo" };

        var result = ReverseHistorySearch.FindPreviousMatch(history, "query", currentSkip: 0);

        Assert.True(result.HasMatch);
        Assert.Equal(1, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal("query foo", result.Match);
    }

    [Fact]
    public void ReverseSearch_FindPreviousMatch_NoMatches_ReturnsEmptyResult()
    {
        var history = new[] { "ls", "echo hi" };

        var result = ReverseHistorySearch.FindPreviousMatch(history, "query", currentSkip: 0);

        Assert.False(result.HasMatch);
        Assert.Equal(-1, result.Index);
        Assert.Equal(0, result.Skip);
        Assert.Equal(string.Empty, result.Match);
    }

    [Fact]
    public void ReverseSearch_IsCaseInsensitive()
    {
        var history = new[] { "QUERY foo" };

        var index = ReverseHistorySearch.FindReverseMatch(history, "query", 0);

        Assert.Equal(0, index);
    }

    [Fact]
    public void ReverseSearch_EmptyQuery_ReturnsMinusOne()
    {
        var history = new[] { "ls", "query foo" };

        var index = ReverseHistorySearch.FindReverseMatch(history, string.Empty, 0);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void ReverseSearch_NoMatches_ReturnsMinusOne()
    {
        var history = new[] { "ls", "echo hi" };

        var index = ReverseHistorySearch.FindReverseMatch(history, "query", 0);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPrompt_WithMatch_UsesNormalState()
    {
        var prompt = ReverseHistorySearch.FormatSearchPrompt("que", "query foo", hasMatch: true);

        Assert.Equal("(reverse-i-search)`que`: query foo", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPrompt_WithoutMatch_UsesFailedState()
    {
        var prompt = ReverseHistorySearch.FormatSearchPrompt("missing", string.Empty, hasMatch: false);

        Assert.Equal("(failed reverse-i-search)`missing`: ", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPrompt_EmptyQuery_UsesNormalState()
    {
        var prompt = ReverseHistorySearch.FormatSearchPrompt(string.Empty, string.Empty, hasMatch: false);

        Assert.Equal("(reverse-i-search)``: ", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPrompt_ForwardSearch_UsesForwardState()
    {
        var prompt = ReverseHistorySearch.FormatSearchPrompt("que", "query foo", hasMatch: true, isForwardSearch: true);

        Assert.Equal("(forward-i-search)`que`: query foo", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPrompt_ForwardSearchWithoutMatch_UsesFailedForwardState()
    {
        var prompt = ReverseHistorySearch.FormatSearchPrompt("missing", string.Empty, hasMatch: false, isForwardSearch: true);

        Assert.Equal("(failed forward-i-search)`missing`: ", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_HighlightsMatchedSubstring()
    {
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup("que", "select query foo", hasMatch: true);

        Assert.Equal("(reverse-i-search)`que`: select [underline yellow]que[/]ry foo", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_IsCaseInsensitive()
    {
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup("que", "SELECT Query foo", hasMatch: true);

        Assert.Equal("(reverse-i-search)`que`: SELECT [underline yellow]Que[/]ry foo", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_HighlightsAllOccurrences()
    {
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup("a", "ababa", hasMatch: true);

        Assert.Equal("(reverse-i-search)`a`: [underline yellow]a[/]b[underline yellow]a[/]b[underline yellow]a[/]", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_FailedState_DoesNotHighlight()
    {
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup("missing", string.Empty, hasMatch: false);

        Assert.Equal("(failed reverse-i-search)`missing`: ", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_EscapesMarkupBracketsInMatch()
    {
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup("foo", "[foo] bar", hasMatch: true);

        Assert.Equal("(reverse-i-search)`foo`: [[[underline yellow]foo[/]]] bar", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_WithSyntaxHighlighter_PreservesCommandStyling()
    {
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup(
            "con",
            "connect",
            hasMatch: true,
            isForwardSearch: false,
            syntaxHighlighter: ShellInterpreter.Instance);

        Assert.StartsWith("(reverse-i-search)`con`: ", prompt);
        Assert.Contains("[underline yellow]con[/]", prompt);
        Assert.DoesNotContain("connect[/]", prompt.Replace("[underline yellow]con[/]nect[/]", string.Empty));
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_WithSyntaxHighlighter_RendersValidSpectreMarkup()
    {
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup(
            "con",
            "connect \"https://example.com:443/\"",
            hasMatch: true,
            isForwardSearch: false,
            syntaxHighlighter: ShellInterpreter.Instance);

        // The body after the prefix must render as Spectre.Console markup without throwing.
        var body = prompt["(reverse-i-search)`con`: ".Length..];
        var ex = Record.Exception(() => new Markup(body).GetSegments(AnsiConsole.Console).ToList());
        Assert.Null(ex);
        Assert.Contains("[underline yellow]con[/]", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_TruncatesToMaxWidthAndKeepsMatchVisible()
    {
        const int MaxWidth = 45;
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup(
            "needle",
            "prefix " + new string('x', 50) + " needle suffix",
            hasMatch: true,
            isForwardSearch: false,
            syntaxHighlighter: ShellInterpreter.Instance,
            maxWidth: MaxWidth);

        var segments = new Markup(prompt).GetSegments(AnsiConsole.Console).ToList();
        var text = string.Concat(segments.Select(segment => segment.Text));

        Assert.True(text.Length <= MaxWidth);
        Assert.Contains("...", text);
        Assert.Contains("needle", text);
        Assert.Contains("[underline yellow]needle[/]", prompt);
    }

    [Fact]
    public void ReverseSearch_FormatSearchPromptMarkup_TruncatesLongQueryToFitMaxWidth()
    {
        const int MaxWidth = 40;
        var longQuery = new string('a', 100);
        var prompt = ReverseHistorySearch.FormatSearchPromptMarkup(
            longQuery,
            "match",
            hasMatch: true,
            isForwardSearch: false,
            syntaxHighlighter: null,
            maxWidth: MaxWidth);

        var text = string.Concat(new Markup(prompt).GetSegments(AnsiConsole.Console).Select(s => s.Text));

        Assert.True(text.Length <= MaxWidth, $"Prompt length {text.Length} exceeded max {MaxWidth}: '{text}'");
        Assert.Contains("...", text);
    }

    private static LineEditorContext CreateContext(string content, int position)
    {
        var buffer = new LineBuffer(content);
        buffer.Move(position);
        return new LineEditorContext(buffer, null!);
    }
}
