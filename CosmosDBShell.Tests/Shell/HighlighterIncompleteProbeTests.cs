// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;

using RadLine;
using Spectre.Console;

/// <summary>
/// Coverage for the tolerant-highlighting work: drives the highlighter with
/// common incomplete constructs and verifies both that the rendered text
/// round-trips through the segments (no duplication / loss) and that known
/// keywords / commands keep their non-error color while the construct is
/// still being typed.
/// </summary>
public class HighlighterIncompleteProbeTests
{
    private static readonly Color ErrorColor = Color.Red;

    [Theory]
    [InlineData("{ echo \"Hello World\"")]
    [InlineData("echo $(44")]
    [InlineData("if ($x > 5) {")]
    [InlineData("while $cond {")]
    [InlineData("def myFunc[$x] {")]
    [InlineData("for $i in [1,2,3] {")]
    [InlineData("loop {")]
    [InlineData("mkitem [1, 2,")]
    [InlineData("echo \"unclosed")]
    [InlineData("if ($x) { echo \"hi\"")]
    public void IncompleteInput_RoundTripsThroughSegments(string input)
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText(input) as Markup;
        Assert.NotNull(res);

        var rendered = string.Concat(res.GetSegments(AnsiConsole.Console).Select(s => s.Text));
        Assert.Equal(input, rendered);
    }

    [Theory]
    [InlineData("if ($x > 5) {", "if")]
    [InlineData("while $cond {", "while")]
    [InlineData("def myFunc[$x] {", "def")]
    [InlineData("for $i in [1,2,3] {", "for")]
    [InlineData("loop {", "loop")]
    [InlineData("{ echo \"Hello World\"", "echo")]
    [InlineData("if ($x) { echo \"hi\"", "if")]
    public void IncompleteInput_KnownKeywordSegmentIsNotErrorColored(string input, string keyword)
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText(input) as Markup;
        Assert.NotNull(res);

        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        var seg = segs.FirstOrDefault(s => s.Text.Trim() == keyword);
        Assert.NotNull(seg);
        Assert.NotEqual(ErrorColor, seg.Style.Foreground);
    }

    [Fact]
    public void SemicolonSeparatedStatements_HighlightEveryCommand()
    {
        const string input = "echo \"foobar\" ; echo \"foobar\"";
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = Assert.IsType<Markup>(highlighter.BuildHighlightedText(input));

        var segs = res.GetSegments(AnsiConsole.Console).ToList();

        // The full line round-trips with no text loss or duplication.
        var rendered = string.Concat(segs.Select(s => s.Text));
        Assert.Equal(input, rendered);

        // Both commands (before and after the ';') must receive identical command
        // highlighting. Before the fix the highlighter stopped at the ';' and the
        // second 'echo' fell through as uncolored plain text.
        var echoSegs = segs.Where(s => s.Text.Trim() == "echo").ToList();
        Assert.Equal(2, echoSegs.Count);
        Assert.NotEqual(ErrorColor, echoSegs[0].Style.Foreground);
        Assert.Equal(echoSegs[0].Style, echoSegs[1].Style);

        // The ';' separator is colored distinctly (its own non-default segment).
        var semicolonSeg = segs.FirstOrDefault(s => s.Text == ";");
        Assert.NotNull(semicolonSeg);
        Assert.NotEqual(Color.Default, semicolonSeg.Style.Foreground);
    }
}
