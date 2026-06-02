// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using System.Text;

/// <summary>
/// Renders a single source line with a caret beneath an error position,
/// expanding tabs to a 4-column tab stop and trimming around the caret with
/// ellipses when the line is too long to display in a normal terminal.
/// </summary>
internal static class SourceCaretRenderer
{
    public const int DefaultMaxDisplayWidth = 100;
    public const int DefaultLeftContextWidth = 40;
    public const int DefaultTabSize = 4;

    /// <summary>
    /// Leading ellipsis prefix (U+2026 HORIZONTAL ELLIPSIS + space) inserted
    /// when content to the left of the caret is elided. The caret row repeats
    /// this same prefix so the marker stays aligned regardless of how wide a
    /// terminal renders the ellipsis glyph.
    /// </summary>
    public const string LeftEllipsis = "\u2026 ";

    /// <summary>
    /// Computes the rendered source line and caret line.
    /// Caret columns are 1-based on the original input line; the returned
    /// values are 1-based on the displayed line.
    /// </summary>
    public static RenderedSourceCaret Render(
        string lineText,
        int caretColumnOneBased,
        int caretLength = 1,
        int maxDisplayWidth = DefaultMaxDisplayWidth,
        int leftContextWidth = DefaultLeftContextWidth,
        int tabSize = DefaultTabSize)
    {
        lineText ??= string.Empty;
        if (caretLength < 1)
        {
            caretLength = 1;
        }

        var (expandedLine, expandedCaret) = ExpandTabs(lineText, caretColumnOneBased, tabSize);
        var (display, displayCaret) = TrimAroundCaret(expandedLine, expandedCaret, maxDisplayWidth, leftContextWidth);

        // When the display was trimmed on the left we reproduce the ellipsis
        // glyph verbatim in the caret row instead of substituting spaces. A
        // bare space is one cell, but some terminals/fonts render U+2026 as a
        // wider (ambiguous-width) glyph; reusing the exact same prefix keeps
        // the caret column aligned with the display in every terminal.
        var caretLeader = display.StartsWith(LeftEllipsis, StringComparison.Ordinal)
            ? LeftEllipsis
            : string.Empty;

        // Clamp caret length so the underline never extends past the displayed text.
        var maxLength = Math.Max(1, display.Length - displayCaret + 1);
        var clampedLength = Math.Min(caretLength, maxLength);
        var caretPad = new string(' ', Math.Max(0, displayCaret - 1 - caretLeader.Length));
        var caretMarker = new string('^', clampedLength);

        return new RenderedSourceCaret(display, displayCaret, expandedCaret, caretLeader, caretPad, caretMarker);
    }

    /// <summary>
    /// Expands hard tabs in <paramref name="lineText"/> to spaces aligned with
    /// the next tab stop, and shifts the caret column past any preceding tab
    /// expansion so it still points at the original character on the display.
    /// </summary>
    public static (string Display, int CaretColumn) ExpandTabs(string lineText, int caretColumnOneBased, int tabSize = DefaultTabSize)
    {
        if (string.IsNullOrEmpty(lineText) || lineText.IndexOf('\t') < 0)
        {
            return (lineText ?? string.Empty, caretColumnOneBased);
        }

        var sb = new StringBuilder(lineText.Length);
        var caret = caretColumnOneBased;
        var visualCol = 0;
        for (int i = 0; i < lineText.Length; i++)
        {
            var c = lineText[i];
            if (c == '\t')
            {
                var spaces = tabSize - (visualCol % tabSize);
                sb.Append(' ', spaces);
                if (caretColumnOneBased > i + 1)
                {
                    caret += spaces - 1;
                }

                visualCol += spaces;
            }
            else
            {
                sb.Append(c);
                visualCol++;
            }
        }

        return (sb.ToString(), caret);
    }

    /// <summary>
    /// Returns a window of the displayed line centred around the caret, with
    /// <c>… </c> ellipses on either side when content is elided. The caret
    /// column is re-mapped into the returned window.
    /// </summary>
    public static (string Display, int CaretColumn) TrimAroundCaret(
        string displayLine,
        int caretColumnOneBased,
        int maxDisplayWidth = DefaultMaxDisplayWidth,
        int leftContextWidth = DefaultLeftContextWidth)
    {
        if (string.IsNullOrEmpty(displayLine) || displayLine.Length <= maxDisplayWidth)
        {
            return (displayLine ?? string.Empty, caretColumnOneBased);
        }

        const string ellipsis = LeftEllipsis; // U+2026 HORIZONTAL ELLIPSIS + space
        const int ellipsisWidth = 2;

        // Pick a content window of up to maxDisplayWidth chars centred so the
        // caret sits ~leftContextWidth from the left edge, then slide the
        // window left if it ran past the end of the line.
        var caretIndex = Math.Clamp(caretColumnOneBased - 1, 0, displayLine.Length);
        var leftIdx = Math.Max(0, caretIndex - leftContextWidth);
        var rightIdx = Math.Min(displayLine.Length, leftIdx + maxDisplayWidth);
        if (rightIdx - leftIdx < maxDisplayWidth)
        {
            leftIdx = Math.Max(0, rightIdx - maxDisplayWidth);
        }

        var leftEllipsis = leftIdx > 0;
        var rightEllipsis = rightIdx < displayLine.Length;

        // Make room for the ellipses by shrinking the corresponding side of
        // the content window, never the opposite side. This keeps right-edge
        // captures from picking up a phantom right ellipsis (and vice versa).
        if (leftEllipsis)
        {
            leftIdx = Math.Min(rightIdx - 1, leftIdx + ellipsisWidth);
        }

        if (rightEllipsis)
        {
            rightIdx = Math.Max(leftIdx + 1, rightIdx - ellipsisWidth);
        }

        // Caret might have been trimmed out of the window in pathological
        // cases; nudge the window so it always contains the caret.
        if (caretIndex < leftIdx)
        {
            leftIdx = caretIndex;
        }

        if (caretIndex >= rightIdx)
        {
            rightIdx = Math.Min(displayLine.Length, caretIndex + 1);
        }

        var sb = new StringBuilder();
        if (leftEllipsis)
        {
            sb.Append(ellipsis);
        }

        sb.Append(displayLine, leftIdx, rightIdx - leftIdx);
        if (rightEllipsis)
        {
            sb.Append(' ').Append('\u2026');
        }

        var newCaret = (caretIndex - leftIdx) + 1 + (leftEllipsis ? ellipsisWidth : 0);
        var maxCaret = caretIndex == displayLine.Length ? sb.Length + 1 : sb.Length;
        newCaret = Math.Clamp(newCaret, 1, maxCaret);
        return (sb.ToString(), newCaret);
    }
}
