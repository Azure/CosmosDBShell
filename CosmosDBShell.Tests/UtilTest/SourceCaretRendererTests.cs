// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Util;

public class SourceCaretRendererTests
{
    [Fact]
    public void Render_ShortLineWithoutTabs_ReturnsLineAndCaretUnchanged()
    {
        var result = SourceCaretRenderer.Render("SELECT * FROM c", caretColumnOneBased: 10);

        Assert.Equal("SELECT * FROM c", result.Display);
        Assert.Equal(10, result.CaretColumn);
        Assert.Equal("         ", result.CaretPad);
        Assert.Equal("^", result.CaretMarker);
    }

    [Fact]
    public void Render_HonorsCaretLength()
    {
        var result = SourceCaretRenderer.Render("SELECT * FROM c", caretColumnOneBased: 15, caretLength: 4);

        Assert.Equal("^", result.CaretMarker[..1]);
        Assert.True(result.CaretMarker.Length <= 4);

        // Underline never extends past the displayed text.
        Assert.True(result.CaretColumn + result.CaretMarker.Length - 1 <= result.Display.Length);
    }

    [Fact]
    public void Render_TabBeforeCaret_AlignsCaretAfterTabExpansion()
    {
        // Raw column 2 is the '}' character; with tabSize=4 it visually sits
        // at column 5.
        var result = SourceCaretRenderer.Render("\t}", caretColumnOneBased: 2);

        Assert.Equal("    }", result.Display);
        Assert.Equal(5, result.CaretColumn);
        Assert.Equal('}', result.Display[result.CaretColumn - 1]);
    }

    [Fact]
    public void Render_VeryLongLine_TrimsAroundCaretWithEllipses()
    {
        var line = new string('a', 80) + "BUG" + new string('a', 80);
        var caretCol = 81; // points at 'B'

        var result = SourceCaretRenderer.Render(line, caretColumnOneBased: caretCol, caretLength: 3, maxDisplayWidth: 60, leftContextWidth: 25);

        Assert.True(result.Display.Length <= 60, $"Display was {result.Display.Length} chars: '{result.Display}'");
        Assert.StartsWith("\u2026 ", result.Display);
        Assert.EndsWith(" \u2026", result.Display);

        // Caret should still land on the 'B' inside the displayed window.
        Assert.Equal('B', result.Display[result.CaretColumn - 1]);
        Assert.Equal("^^^", result.CaretMarker);
    }

    [Fact]
    public void Render_LongLineCaretNearStart_OnlyRightEllipsis()
    {
        var line = "BUG" + new string('a', 200);

        var result = SourceCaretRenderer.Render(line, caretColumnOneBased: 1, caretLength: 3, maxDisplayWidth: 60, leftContextWidth: 25);

        Assert.DoesNotContain('\u2026', result.Display[..2]);
        Assert.EndsWith(" \u2026", result.Display);
        Assert.Equal(1, result.CaretColumn);
    }

    [Fact]
    public void Render_LongLineCaretNearEnd_OnlyLeftEllipsis()
    {
        var line = new string('a', 200) + "BUG";
        var caretCol = line.Length - 2; // points at the 'B' near end

        var result = SourceCaretRenderer.Render(line, caretColumnOneBased: caretCol, caretLength: 3, maxDisplayWidth: 60, leftContextWidth: 25);

        Assert.StartsWith("\u2026 ", result.Display);
        Assert.DoesNotContain('\u2026', result.Display[^2..]);
        Assert.Equal('B', result.Display[result.CaretColumn - 1]);
    }

    [Fact]
    public void TrimAroundCaret_LineShorterThanWidth_ReturnsUnchanged()
    {
        var (display, caret) = SourceCaretRenderer.TrimAroundCaret("hello world", 5, maxDisplayWidth: 100);

        Assert.Equal("hello world", display);
        Assert.Equal(5, caret);
    }
}
