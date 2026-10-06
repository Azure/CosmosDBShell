// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Core;
using Spectre.Console;

public class ShellOutputTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Presentation_UsesCategoryPolicyForTextMarkupAndRenderables(bool quiet, bool stdio)
    {
        foreach (var kind in Enum.GetValues<ShellMessageKind>())
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var output = CreateOutput(quiet, stdio, stdout, stderr);
            var marker = kind.ToString();

            output.Write(kind, marker + "_text");
            output.WriteLine(kind, marker + "_line");
            output.Markup(kind, $"[red]{marker}_markup[/]");
            output.MarkupLine(kind, $"[red]{marker}_markupLine[/]");
            output.Render(kind, new Text(marker + "_renderable"));
            output.RenderLine(kind, marker + "_renderLine");
            output.WriteException(kind, new InvalidOperationException(marker + "_exception"));

            // CreateOutput treats quiet or stdio as machine mode, which suppresses command-level
            // result presentation; machine-mode results go through WriteResult instead.
            var machineMode = quiet || stdio;
            var suppressed = (quiet && kind is ShellMessageKind.Information or ShellMessageKind.Progress)
                || ((stdio || machineMode) && kind == ShellMessageKind.Result);
            if (suppressed)
            {
                Assert.Empty(stdout.ToString());
                Assert.Empty(stderr.ToString());
                continue;
            }

            var useStderr = stdio || (quiet && kind is ShellMessageKind.Warning or ShellMessageKind.Error or ShellMessageKind.RequiredInstruction);
            var visible = (useStderr ? stderr : stdout).ToString();
            Assert.Empty((useStderr ? stdout : stderr).ToString());
            foreach (var suffix in new[] { "_text", "_line", "_markup", "_markupLine", "_renderable", "_renderLine", "_exception" })
            {
                Assert.Contains(marker + suffix, visible);
            }

            if (machineMode)
            {
                Assert.DoesNotContain("\u001b", visible, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void StructuredMode_RoutesWarningsErrorsAndAuthenticationToStderr()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var output = CreateOutput(false, false, stdout, stderr, machineMode: true);
        output.MarkupLine(ShellMessageKind.Warning, "[yellow]warning[/]");
        output.WriteLine(ShellMessageKind.Error, "error");
        output.WriteLine(ShellMessageKind.RequiredInstruction, "login");
        output.WriteLine(ShellMessageKind.Result, "eager table");
        output.Render(ShellMessageKind.Result, new Text("eager renderable"));
        output.WriteResult("{\"result\":true}");
        Assert.Equal("{\"result\":true}" + Environment.NewLine, stdout.ToString());
        Assert.DoesNotContain("eager", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("warning", stderr.ToString());
        Assert.Contains("error", stderr.ToString());
        Assert.Contains("login", stderr.ToString());
        Assert.DoesNotContain("\u001b", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Modes_AreEvaluatedAtWriteTime()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var quiet = false;
        var stdio = false;
        var output = new ShellOutput(() => quiet, () => stdio, () => quiet || stdio, () => stdout, () => stderr);
        output.WriteLine(ShellMessageKind.Information, "before");
        quiet = true;
        output.WriteLine(ShellMessageKind.Information, "hidden");
        output.WriteLine(ShellMessageKind.Result, "command-level result hidden in machine mode");
        output.WriteResult("result");
        stdio = true;
        output.WriteResult("not a protocol response");
        output.WriteLine(ShellMessageKind.RequiredInstruction, "device code");
        Assert.Equal("before" + Environment.NewLine + "result" + Environment.NewLine, stdout.ToString());
        Assert.Equal("device code" + Environment.NewLine, stderr.ToString());
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ClearScreen_OnlyTouchesInteractiveTerminal(bool machineMode, bool stdio, bool cleared)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var terminal = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(stdout),
        });
        var output = new ShellOutput(() => false, () => stdio, () => machineMode, () => stdout, () => stderr, () => terminal);
        output.ClearScreen();
        Assert.Equal(cleared, stdout.ToString().Length > 0);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Text_PreservesBracesAndExplicitFormatting()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var output = CreateOutput(false, false, stdout, stderr);
        output.WriteLine(ShellMessageKind.Result, "{\"value\":1}");
        output.Write(ShellMessageKind.Result, "{0}={1}", "value", 2);
        Assert.Equal("{\"value\":1}" + Environment.NewLine + "value=2", stdout.ToString());
    }

    [Fact]
    public void Markup_PreservesLiteralBracesWithoutFormattingArguments()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var output = CreateOutput(false, false, stdout, stderr);
        output.Markup(ShellMessageKind.Result, "[red]{\"value\":1}[/]");
        output.MarkupLine(ShellMessageKind.Result, "[red]{\"value\":2}[/]");
        output.MarkupLine(ShellMessageKind.Result, "[red]{0}[/]", "formatted");
        Assert.Equal("{\"value\":1}{\"value\":2}" + Environment.NewLine + "formatted" + Environment.NewLine, stdout.ToString());
    }

    private static ShellOutput CreateOutput(bool quiet, bool stdio, StringWriter stdout, StringWriter stderr, bool machineMode = false)
        => new(
            () => quiet,
            () => stdio,
            () => machineMode || quiet || stdio,
            () => stdout,
            () => stderr,
            () => AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(stdout),
            }));
}
