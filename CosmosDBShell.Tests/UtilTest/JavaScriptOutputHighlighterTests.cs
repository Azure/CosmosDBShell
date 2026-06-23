// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Core;

namespace CosmosShell.Tests.UtilTest;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class JavaScriptOutputHighlighterTests
{
    [Fact]
    public void Keywords_AreColoredWithKeywordTheme()
    {
        var markup = JavaScriptOutputHighlighter.BuildMarkup("function foo() { return bar; }");

        Assert.Contains($"[{Theme.KeywordColorName}]function[/]", markup);
        Assert.Contains($"[{Theme.KeywordColorName}]return[/]", markup);

        // Non-keyword identifiers stay plain (no keyword markup around them).
        Assert.DoesNotContain($"[{Theme.KeywordColorName}]foo[/]", markup);
        Assert.DoesNotContain($"[{Theme.KeywordColorName}]bar[/]", markup);
    }

    [Fact]
    public void StringsAndTemplateLiterals_UseLiteralColor()
    {
        var markup = JavaScriptOutputHighlighter.BuildMarkup("var s = 'hi'; var t = `bye`;");

        Assert.Contains($"[{Theme.LiteralColorName}]'hi'[/]", markup);
        Assert.Contains($"[{Theme.LiteralColorName}]`bye`[/]", markup);
    }

    [Fact]
    public void Numbers_UseLiteralColor()
    {
        var markup = JavaScriptOutputHighlighter.BuildMarkup("var x = 42;");

        Assert.Contains($"[{Theme.LiteralColorName}]42[/]", markup);
    }

    [Fact]
    public void Comments_UseMutedColor()
    {
        var markup = JavaScriptOutputHighlighter.BuildMarkup("// line\n/* block */ x");

        Assert.Contains($"[{Theme.MutedColorName}]// line[/]", markup);
        Assert.Contains($"[{Theme.MutedColorName}]/* block */[/]", markup);
    }

    [Fact]
    public void PlainText_IsMarkupEscaped()
    {
        // Square brackets must be escaped so Spectre.Console does not treat them as markup.
        var markup = JavaScriptOutputHighlighter.BuildMarkup("a[0]");

        Assert.Contains("[[", markup);
        Assert.Contains("]]", markup);
    }

    [Fact]
    public void NestedBrackets_CycleColorsByDepth()
    {
        // depth 0 -> '(', depth 1 -> '{', depth 2 -> '[' (next nested opener).
        var markup = JavaScriptOutputHighlighter.BuildMarkup("f({ a: [1] })");

        Assert.Contains($"[{Theme.GetBracketColor(0)}]([/]", markup);
        Assert.Contains($"[{Theme.GetBracketColor(1)}]{{[/]", markup);
        Assert.Contains($"[{Theme.GetBracketColor(2)}][[[/]", markup);

        // Closing brackets reuse the color of their matching opener.
        Assert.Contains($"[{Theme.GetBracketColor(2)}]]][/]", markup);
        Assert.Contains($"[{Theme.GetBracketColor(1)}]}}[/]", markup);
        Assert.Contains($"[{Theme.GetBracketColor(0)}])[/]", markup);
    }

    [Fact]
    public void EmptySource_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, JavaScriptOutputHighlighter.BuildMarkup(string.Empty));
    }
}
