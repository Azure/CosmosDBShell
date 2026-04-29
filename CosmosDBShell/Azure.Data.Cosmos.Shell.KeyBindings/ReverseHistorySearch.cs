//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using System;
using System.Collections.Generic;
using System.Text;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

internal static class ReverseHistorySearch
{
    public static int FindReverseMatch(IReadOnlyList<string> history, string query, int skipMatches)
    {
        if (string.IsNullOrEmpty(query))
        {
            return -1;
        }

        var matchesSeen = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                if (matchesSeen == skipMatches)
                {
                    return i;
                }

                matchesSeen++;
            }
        }

        return -1;
    }

    public static ReverseHistorySearchResult FindInitialMatch(IReadOnlyList<string> history, string query)
    {
        return CreateResult(history, query, skip: 0);
    }

    public static ReverseHistorySearchResult FindInitialForwardMatch(IReadOnlyList<string> history, string query)
    {
        var matchCount = CountMatches(history, query);
        return matchCount > 0 ? CreateResult(history, query, skip: matchCount - 1) : ReverseHistorySearchResult.None();
    }

    public static ReverseHistorySearchResult FindNextMatch(IReadOnlyList<string> history, string query, int currentSkip)
    {
        var nextSkip = currentSkip + 1;
        var next = CreateResult(history, query, nextSkip);
        if (next.HasMatch)
        {
            return next;
        }

        return CreateResult(history, query, skip: 0);
    }

    public static ReverseHistorySearchResult FindPreviousMatch(IReadOnlyList<string> history, string query, int currentSkip)
    {
        var matchCount = CountMatches(history, query);
        if (matchCount <= 0)
        {
            return ReverseHistorySearchResult.None();
        }

        var previousSkip = currentSkip <= 0 ? matchCount - 1 : currentSkip - 1;
        return CreateResult(history, query, previousSkip);
    }

    public static string FormatSearchPrompt(string query, string match, bool hasMatch, bool isForwardSearch = false)
    {
        var prefix = GetSearchPrefix(query, hasMatch, isForwardSearch);
        return $"({prefix})`{query}`: {match}";
    }

    public static string FormatSearchPromptMarkup(string query, string match, bool hasMatch, bool isForwardSearch = false)
    {
        return FormatSearchPromptMarkup(query, match, hasMatch, isForwardSearch, syntaxHighlighter: null);
    }

    public static string FormatSearchPromptMarkup(string query, string match, bool hasMatch, bool isForwardSearch, ShellInterpreter? syntaxHighlighter)
    {
        return FormatSearchPromptMarkup(query, match, hasMatch, isForwardSearch, syntaxHighlighter, maxWidth: null);
    }

    public static string FormatSearchPromptMarkup(string query, string match, bool hasMatch, bool isForwardSearch, ShellInterpreter? syntaxHighlighter, int? maxWidth)
    {
        var prefix = GetSearchPrefix(query, hasMatch, isForwardSearch);
        var plainPrefix = $"({prefix})`{query}`: ";
        int? maxMatchLength = maxWidth.HasValue ? Math.Max(0, maxWidth.Value - plainPrefix.Length) : null;
        var renderedMatch = HighlightMatch(TruncateMatch(match, query, hasMatch, maxMatchLength), query, hasMatch, syntaxHighlighter);
        return $"({prefix})`{Markup.Escape(query)}`: {renderedMatch}";
    }

    private static string GetSearchPrefix(string query, bool hasMatch, bool isForwardSearch)
    {
        if (!string.IsNullOrEmpty(query) && !hasMatch)
        {
            return MessageService.GetString(isForwardSearch ? "history-search-failed-forward" : "history-search-failed-reverse");
        }

        return MessageService.GetString(isForwardSearch ? "history-search-forward" : "history-search-reverse");
    }

    private static string TruncateMatch(string match, string query, bool hasMatch, int? maxLength)
    {
        const string Marker = "...";

        if (!maxLength.HasValue || match.Length <= maxLength.Value)
        {
            return match;
        }

        if (maxLength.Value <= 0)
        {
            return string.Empty;
        }

        if (maxLength.Value <= Marker.Length)
        {
            return match[..maxLength.Value];
        }

        var contentLength = maxLength.Value - Marker.Length;
        if (!hasMatch || string.IsNullOrEmpty(query))
        {
            return match[..contentLength] + Marker;
        }

        var hit = match.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (hit < 0 || hit <= contentLength)
        {
            return match[..contentLength] + Marker;
        }

        var start = Math.Min(hit, match.Length - contentLength);
        return Marker + match.Substring(start, contentLength);
    }

    private static string HighlightMatch(string match, string query, bool hasMatch, ShellInterpreter? syntaxHighlighter)
    {
        if (string.IsNullOrEmpty(match))
        {
            return string.Empty;
        }

        var styledMarkup = syntaxHighlighter != null
            ? SafeBuildHighlightedMarkup(syntaxHighlighter, match)
            : Markup.Escape(match);

        if (!hasMatch || string.IsNullOrEmpty(query))
        {
            return styledMarkup;
        }

        var ranges = FindAllMatches(match, query);
        if (ranges.Count == 0)
        {
            return styledMarkup;
        }

        return OverlayUnderline(styledMarkup, ranges);
    }

    private static string SafeBuildHighlightedMarkup(ShellInterpreter highlighter, string text)
    {
        try
        {
            return highlighter.BuildHighlightedMarkup(text);
        }
        catch (Exception)
        {
            return Markup.Escape(text);
        }
    }

    private static List<(int Start, int End)> FindAllMatches(string match, string query)
    {
        var ranges = new List<(int, int)>();
        var index = 0;
        while (index < match.Length)
        {
            var hit = match.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                break;
            }

            ranges.Add((hit, hit + query.Length));
            index = hit + query.Length;
        }

        return ranges;
    }

    /// <summary>
    /// Walks a Spectre.Console markup string and wraps the plain-text positions in <paramref name="ranges"/>
    /// with [underline yellow]...[/]. Underline is closed before any markup tag boundary and reopened on the
    /// other side so that the output remains well-nested.
    /// </summary>
    private static string OverlayUnderline(string markup, IReadOnlyList<(int Start, int End)> ranges)
    {
        const string OpenUnderline = "[underline yellow]";
        const string CloseUnderline = "[/]";

        var sb = new StringBuilder(markup.Length + (ranges.Count * (OpenUnderline.Length + CloseUnderline.Length)));
        var plainIndex = 0;
        var inUnderline = false;
        var i = 0;

        bool ShouldUnderline(int p)
        {
            foreach (var r in ranges)
            {
                if (p >= r.Start && p < r.End)
                {
                    return true;
                }
            }

            return false;
        }

        while (i < markup.Length)
        {
            var c = markup[i];

            if (c == '[' && i + 1 < markup.Length && markup[i + 1] == '[')
            {
                // Escaped literal '['
                if (!inUnderline && ShouldUnderline(plainIndex))
                {
                    sb.Append(OpenUnderline);
                    inUnderline = true;
                }

                sb.Append("[[");
                i += 2;
                plainIndex++;
                if (inUnderline && !ShouldUnderline(plainIndex))
                {
                    sb.Append(CloseUnderline);
                    inUnderline = false;
                }

                continue;
            }

            if (c == ']' && i + 1 < markup.Length && markup[i + 1] == ']')
            {
                // Escaped literal ']'
                if (!inUnderline && ShouldUnderline(plainIndex))
                {
                    sb.Append(OpenUnderline);
                    inUnderline = true;
                }

                sb.Append("]]");
                i += 2;
                plainIndex++;
                if (inUnderline && !ShouldUnderline(plainIndex))
                {
                    sb.Append(CloseUnderline);
                    inUnderline = false;
                }

                continue;
            }

            if (c == '[')
            {
                // Markup tag (open or close). Close underline before the tag, copy verbatim, reopen after if still in match.
                if (inUnderline)
                {
                    sb.Append(CloseUnderline);
                    inUnderline = false;
                }

                var tagEnd = markup.IndexOf(']', i + 1);
                if (tagEnd < 0)
                {
                    // Malformed; copy rest verbatim.
                    sb.Append(markup, i, markup.Length - i);
                    return sb.ToString();
                }

                sb.Append(markup, i, tagEnd - i + 1);
                i = tagEnd + 1;
                if (ShouldUnderline(plainIndex))
                {
                    sb.Append(OpenUnderline);
                    inUnderline = true;
                }

                continue;
            }

            // Plain character
            if (!inUnderline && ShouldUnderline(plainIndex))
            {
                sb.Append(OpenUnderline);
                inUnderline = true;
            }

            sb.Append(c);
            i++;
            plainIndex++;
            if (inUnderline && !ShouldUnderline(plainIndex))
            {
                sb.Append(CloseUnderline);
                inUnderline = false;
            }
        }

        if (inUnderline)
        {
            sb.Append(CloseUnderline);
        }

        return sb.ToString();
    }

    private static int CountMatches(IReadOnlyList<string> history, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        var count = 0;
        foreach (var item in history)
        {
            if (item.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static ReverseHistorySearchResult CreateResult(IReadOnlyList<string> history, string query, int skip)
    {
        var index = FindReverseMatch(history, query, skip);
        return index >= 0 ? new ReverseHistorySearchResult(index, skip, history[index]) : ReverseHistorySearchResult.None(skip);
    }
}
