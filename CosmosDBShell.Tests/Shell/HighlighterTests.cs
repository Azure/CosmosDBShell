// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;

using RadLine;
using Spectre.Console;

public class HighlighterTests
{
    [Fact]
    public void TestCommandHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("help") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.Single(segs);
        Assert.Equal("help", segs[0].Text);
    }

    [Fact]
    public void TestEscapeHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("help \"a\"") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.Equal(3, segs.Count);
        Assert.Equal("help", segs[0].Text.Trim());
        Assert.Equal("", segs[1].Text.Trim());
        Assert.Equal("\"a\"", segs[2].Text.Trim());
    }
    static readonly Color ErrorColor = Color.Red;

    [Fact]
    public void TestUnknownCommandHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("unknowncommand arg1") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Equal("unknowncommand", segs[0].Text.Trim());
        // Unknown commands should have error color
        Assert.Equal(ErrorColor, segs[0].Style.Foreground);
    }

    [Fact]
    public void TestJsonExpressionHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("mkitem {\"name\": \"John\", \"age\": 30}") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();

        // Should have segments for: mkitem, space, {, "name", :, space, "John", etc.
        Assert.True(segs.Count > 5);
        Assert.Equal("mkitem", segs[0].Text.Trim());

        // Find segments containing braces and colons - they should be highlighted
        var braceSegments = segs.Where(s => s.Text.Contains("{") || s.Text.Contains("}")).ToList();
        Assert.NotEmpty(braceSegments);

        var colonSegments = segs.Where(s => s.Text.Contains(":")).ToList();
        Assert.NotEmpty(colonSegments);
    }

    [Fact]
    public void TestJsonArrayHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("mkitem [1, 2, 3]") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();

        // Should have segments for brackets and commas
        Assert.True(segs.Count > 3);
        Assert.Equal("mkitem", segs[0].Text.Trim());

        // Find segments containing brackets
        var bracketSegments = segs.Where(s => s.Text.Contains("[") || s.Text.Contains("]")).ToList();
        Assert.NotEmpty(bracketSegments);
    }

    [Fact]
    public void TestVariableHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("echo $myVariable") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Equal("echo", segs[0].Text.Trim());
        Assert.Contains(" $myVariable", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestInterpolatedStringHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("echo $\"Hello $name\"") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Equal("echo", segs[0].Text.Trim());
    }

    [Fact]
    public void TestExpressionHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("echo $(2 + 3)") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Equal("echo", segs[0].Text.Trim());
    }

    [Fact]
    public void TestOptionHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("ls --max 10") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 3);
        Assert.Equal("ls", segs[0].Text.Trim());

        // Find the option segment
        var optionSeg = segs.FirstOrDefault(s => s.Text.Contains("--max"));
        Assert.NotNull(optionSeg);
    }

    [Fact]
    public void TestInvalidOptionHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("ls --invalidoption") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);

        // Invalid option should have error color
        var optionSeg = segs.FirstOrDefault(s => s.Text.Contains("--invalidoption"));
        Assert.NotNull(optionSeg);
        Assert.Equal(ErrorColor, optionSeg.Style.Foreground);
    }

    [Fact]
    public void TestShortOptionAliasHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("mkcon foobar /id -ip \"{}\"") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();

        var optionSeg = segs.FirstOrDefault(s => s.Text.Contains("-ip"));
        Assert.NotNull(optionSeg);
        Assert.NotEqual(ErrorColor, optionSeg.Style.Foreground);
    }

    [Fact]
    public void TestPipeStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("ls | jq .name") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 4);
        Assert.Equal("ls", segs[0].Text.Trim());

        // Should have pipe symbol
        Assert.Contains(" | ", segs.Select(s => s.Text));

        // Second command should also be highlighted
        var jqSeg = segs.FirstOrDefault(s => s.Text.Contains("jq"));
        Assert.NotNull(jqSeg);
    }

    [Fact]
    public void TestComplexExpressionHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("echo $(2 + 3 * 4)") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Equal("echo", segs[0].Text.Trim());
    }

    [Fact]
    public void TestNestedJsonHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("mkitem {\"user\": {\"name\": \"John\", \"age\": 30}}") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count > 5);

        // Should have multiple brace segments for nested objects
        var braceSegments = segs.Where(s => s.Text.Contains("{") || s.Text.Contains("}")).ToList();
        Assert.True(braceSegments.Count >= 2);
    }

    [Fact]
    public void TestPartialInputHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        // Test partial command
        var res = highlighter.BuildHighlightedText("hel") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.Single(segs);
        Assert.Equal("hel", segs[0].Text);

        // Test incomplete JSON
        res = highlighter.BuildHighlightedText("mkitem {\"name\"") as Markup;
        Assert.NotNull(res);
        segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 1);
    }

    [Fact]
    public void TestSpecialCharactersEscaping()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("echo \"<>&\"") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);

        // Verify special characters are properly escaped
        var stringContent = string.Join("", segs.Select(s => s.Text));
        Assert.Contains("<>&", stringContent);
    }

    [Fact]
    public void TestMultipleOptionsHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("query --max 10 --format json") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 5);
        Assert.Equal("query", segs[0].Text.Trim());

        // Combine all segment texts to check for presence of options and values
        var fullText = string.Join("", segs.Select(s => s.Text));

        // Should have both options and their values in the full text
        Assert.Contains("--max", fullText);
        Assert.Contains("10", fullText);
        Assert.Contains("--format", fullText);
        Assert.Contains("json", fullText);

        // Also verify that --max and --format appear as separate segments
        Assert.Contains("--max", segs.Select(s => s.Text));
        Assert.Contains("--format", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestBooleanLiteralsHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("echo true false") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 3);
        Assert.Equal("echo", segs[0].Text.Trim());
        Assert.Contains("true", segs.Select(s => s.Text));
        Assert.Contains("false", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestNumericLiteralsHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("echo 42 3.14 1.23e4") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Equal("echo", segs[0].Text.Trim());
    }

    [Fact]
    public void TestAssignmentStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("$x = 42") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 3);
        Assert.Contains("$x ", segs.Select(s => s.Text));
        Assert.Contains("=", segs.Select(s => s.Text));
        Assert.Contains("42", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestIfStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("if ($x > 10) echo \"big\"") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 4);
        Assert.Contains("if", segs.Select(s => s.Text));
        Assert.Contains("echo", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestIfElseStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("if ($x > 10) { echo \"big\" } else { echo \"small\" }") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 6);
        Assert.Contains("if", segs.Select(s => s.Text));
        Assert.Contains("else", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestWhileStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("while ($i < 10) echo $i") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 4);
        Assert.Contains("while", segs.Select(s => s.Text));
        Assert.Contains("echo", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestDoWhileStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("do { echo $i } while ($i < 10)") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Contains("do", segs.Select(s => s.Text));
        Assert.Contains("while", segs.Select(s => s.Text));
        Assert.Contains("echo", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestForStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("for $item in [1, 2, 3] echo $item") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 5);
        Assert.Contains("for", segs.Select(s => s.Text));
        Assert.Contains("in", segs.Select(s => s.Text));
        Assert.Contains("echo", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestLoopStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("loop echo \"infinite\"") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Contains("loop", segs.Select(s => s.Text));
        Assert.Contains("echo", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestBlockStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("{ echo \"one\"; echo \"two\" }") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 4);
        Assert.Contains("{ ", segs.Select(s => s.Text));
        Assert.Contains(" }", segs.Select(s => s.Text));
        var echoSegs = segs.Where(s => s.Text.Contains("echo")).ToList();
        Assert.Equal(2, echoSegs.Count);
    }

    [Fact]
    public void TestDefStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("def myFunc[$x] echo $x") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Contains("def", segs.Select(s => s.Text));
        Assert.Contains(" myFunc[$x] ", segs.Select(s => s.Text));
        Assert.Contains("echo", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestReturnStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("return 42") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Contains("return", segs.Select(s => s.Text));
        Assert.Contains("42", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestBreakStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("break") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 1);
        Assert.Contains("break", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestContinueStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("continue") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 1);
        Assert.Contains("continue", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestComplexPipelineHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("ls | jq .name | echo") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 3);
        Assert.Contains("ls", segs.Select(s => s.Text));
        Assert.Contains("jq", segs.Select(s => s.Text));
        Assert.Contains("echo", segs.Select(s => s.Text));
        var pipeSegs = segs.Where(s => s.Text.Contains("|")).ToList();
        Assert.Equal(2, pipeSegs.Count);
    }

    [Fact]
    public void TestNestedBlocksHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("{ if ($x > 0) { echo \"positive\" } }") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 6);
        var braceSegs = segs.Where(s => s.Text.Contains("{") || s.Text.Contains("}")).ToList();
        Assert.True(braceSegs.Count >= 3); // Two pairs of braces
    }

    [Fact]
    public void TestRedirectHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("ls > output.txt") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Contains("ls", segs.Select(s => s.Text));
        Assert.Contains(">", segs.Select(s => s.Text));
        Assert.Contains("output.txt", segs.Select(s => s.Text));

        var dest = segs.First(s => s.Text == "output.txt");
        Assert.True(dest.Style.Decoration.HasFlag(Decoration.Underline));
    }

    [Fact]
    public void TestMultipleRedirectsHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("ls > output.txt err> error.txt") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 5);
        Assert.Contains(">", segs.Select(s => s.Text));
        Assert.Contains("err>", segs.Select(s => s.Text));
        Assert.Contains("output.txt", segs.Select(s => s.Text));
        Assert.Contains("error.txt", segs.Select(s => s.Text));

        foreach (var dest in new[] { "output.txt", "error.txt" })
        {
            var seg = segs.First(s => s.Text == dest);
            Assert.True(seg.Style.Decoration.HasFlag(Decoration.Underline), $"Expected {dest} to be underlined.");
        }
    }

    [Fact]
    public void TestComplexControlFlowHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("for $i in [1, 2, 3] { if ($i > 1) break }") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 6);
        Assert.Contains("for", segs.Select(s => s.Text));
        Assert.Contains("if", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestExpressionInStatementHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("$result = 2 + 3 * 4") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.Contains("$result ", segs.Select(s => s.Text));
        Assert.Contains("=", segs.Select(s => s.Text));
        Assert.Contains("+", segs.Select(s => s.Text));
        Assert.Contains("*", segs.Select(s => s.Text));
    }

    [Fact]
    public void TestJsonPathVariableHighlight()
    {
        var highlighter = (IHighlighter)ShellInterpreter.Instance;

        var res = highlighter.BuildHighlightedText("echo $.users[0].name") as Markup;
        Assert.NotNull(res);
        var segs = res.GetSegments(AnsiConsole.Console).ToList();
        Assert.True(segs.Count >= 2);
        Assert.Contains("echo", segs.Select(s => s.Text));
        Assert.Contains(" $.users[0].name", segs.Select(s => s.Text));
    }
}
