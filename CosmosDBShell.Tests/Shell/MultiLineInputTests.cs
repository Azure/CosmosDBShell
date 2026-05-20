// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System.Text;

using Azure.Data.Cosmos.Shell.Core;

public class MultiLineInputTests
{
    [Fact]
    public void IsIncompleteInput_EmptyString_ReturnsFalse()
    {
        Assert.False(ShellInterpreter.IsIncompleteInput(string.Empty));
        Assert.False(ShellInterpreter.IsIncompleteInput("   "));
    }

    [Fact]
    public void IsIncompleteInput_CompleteCommand_ReturnsFalse()
    {
        Assert.False(ShellInterpreter.IsIncompleteInput("ls"));
        Assert.False(ShellInterpreter.IsIncompleteInput("help"));
    }

    [Fact]
    public void IsIncompleteInput_UnclosedBrace_ReturnsTrue()
    {
        Assert.True(ShellInterpreter.IsIncompleteInput("if true {"));
    }

    [Fact]
    public void IsIncompleteInput_UnterminatedDoubleQuotedString_ReturnsTrue()
    {
        Assert.True(ShellInterpreter.IsIncompleteInput("echo \"hello"));
    }

    [Fact]
    public void IsIncompleteInput_UnterminatedSingleQuotedString_ReturnsTrue()
    {
        Assert.True(ShellInterpreter.IsIncompleteInput("echo 'hello"));
    }

    [Fact]
    public void IsIncompleteInput_BalancedBraces_ReturnsFalse()
    {
        Assert.False(ShellInterpreter.IsIncompleteInput("if true { echo hi }"));
    }

    [Fact]
    public void IsIncompleteInput_ClosedString_ReturnsFalse()
    {
        Assert.False(ShellInterpreter.IsIncompleteInput("echo \"hello\""));
    }

    [Fact]
    public void TryRemoveLineContinuation_OddTrailingBackslash_RemovesOneBackslash()
    {
        var line = "echo hello\\";

        Assert.True(ShellInterpreter.TryRemoveLineContinuation(ref line));
        Assert.Equal("echo hello", line);
    }

    [Fact]
    public void TryRemoveLineContinuation_EvenTrailingBackslashes_KeepsLiteralBackslashes()
    {
        var line = "echo hello\\\\";

        Assert.False(ShellInterpreter.TryRemoveLineContinuation(ref line));
        Assert.Equal("echo hello\\\\", line);
    }

    [Fact]
    public void AppendMultiLineFragment_BackslashContinuation_SplicesPhysicalLines()
    {
        var buffer = new StringBuilder("echo hello");

        ShellInterpreter.AppendMultiLineFragment(buffer, " world", suppressNewline: true);

        Assert.Equal("echo hello world", buffer.ToString());
    }

    [Fact]
    public void AppendMultiLineFragment_ParseContinuation_PreservesNewline()
    {
        var buffer = new StringBuilder("if true {");

        ShellInterpreter.AppendMultiLineFragment(buffer, "echo hello", suppressNewline: false);

        Assert.Equal("if true {\necho hello", buffer.ToString());
    }

    [Fact]
    public void DecodeHistoryLine_PreviousRawBackslashNEntry_RemainsLiteral()
    {
        Assert.Equal("echo \\n", ShellInterpreter.DecodeHistoryLine("echo \\n"));
    }

    [Fact]
    public void EncodeHistoryLine_MultiLineEntry_RoundTrips()
    {
        var command = "if true {\necho \\n\r}";

        Assert.Equal(command, ShellInterpreter.DecodeHistoryLine(ShellInterpreter.EncodeHistoryLine(command)));
    }

    [Fact]
    public void EncodeHistoryLine_SingleLineBackslashEntry_StaysReadable()
    {
        Assert.Equal("echo \\n", ShellInterpreter.EncodeHistoryLine("echo \\n"));
    }

    [Fact]
    public void ProcessInteractiveLine_CompleteSingleLine_ReturnsItImmediately()
    {
        StringBuilder? buffer = null;
        var suppress = false;

        var command = ShellInterpreter.ProcessInteractiveLine("ls", ref buffer, ref suppress);

        Assert.Equal("ls", command);
        Assert.Null(buffer);
        Assert.False(suppress);
    }

    [Fact]
    public void ProcessInteractiveLine_UnclosedBraceThenClose_AccumulatesAndJoinsWithNewlines()
    {
        StringBuilder? buffer = null;
        var suppress = false;

        var first = ShellInterpreter.ProcessInteractiveLine("if true {", ref buffer, ref suppress);
        Assert.Null(first);
        Assert.NotNull(buffer);

        var second = ShellInterpreter.ProcessInteractiveLine("    echo hello", ref buffer, ref suppress);
        Assert.Null(second);

        var third = ShellInterpreter.ProcessInteractiveLine("}", ref buffer, ref suppress);
        Assert.Equal("if true {\n    echo hello\n}", third);
        Assert.Null(buffer);
        Assert.False(suppress);
    }

    [Fact]
    public void ProcessInteractiveLine_BackslashContinuation_SplicesWithoutInsertingNewlines()
    {
        StringBuilder? buffer = null;
        var suppress = false;

        var first = ShellInterpreter.ProcessInteractiveLine("echo hello\\", ref buffer, ref suppress);
        Assert.Null(first);
        Assert.NotNull(buffer);
        Assert.True(suppress);

        var second = ShellInterpreter.ProcessInteractiveLine(" world", ref buffer, ref suppress);
        Assert.Equal("echo hello world", second);
        Assert.Null(buffer);
        Assert.False(suppress);
    }

    [Fact]
    public void ProcessInteractiveLine_MixedBackslashThenParseContinuation_JoinsBothStyles()
    {
        StringBuilder? buffer = null;
        var suppress = false;

        // Backslash continuation: next fragment splices without a newline.
        Assert.Null(ShellInterpreter.ProcessInteractiveLine("if true \\", ref buffer, ref suppress));
        Assert.True(suppress);

        // The next line opens a block; from here on the input is incomplete because of
        // the parser, not the backslash, so newlines must be preserved between fragments.
        Assert.Null(ShellInterpreter.ProcessInteractiveLine("{", ref buffer, ref suppress));
        Assert.False(suppress);

        Assert.Null(ShellInterpreter.ProcessInteractiveLine("    echo hi", ref buffer, ref suppress));
        var command = ShellInterpreter.ProcessInteractiveLine("}", ref buffer, ref suppress);
        Assert.Equal("if true {\n    echo hi\n}", command);
        Assert.Null(buffer);
        Assert.False(suppress);
    }

    [Fact]
    public void ProcessInteractiveLine_UnterminatedStringSpanningTwoLines_JoinsWithNewline()
    {
        StringBuilder? buffer = null;
        var suppress = false;

        Assert.Null(ShellInterpreter.ProcessInteractiveLine("echo \"hello", ref buffer, ref suppress));
        var command = ShellInterpreter.ProcessInteractiveLine("world\"", ref buffer, ref suppress);

        Assert.Equal("echo \"hello\nworld\"", command);
        Assert.Null(buffer);
    }

    [Fact]
    public void ProcessInteractiveLine_NullInputMidBuffer_DiscardsBuffer()
    {
        StringBuilder? buffer = null;
        var suppress = false;

        Assert.Null(ShellInterpreter.ProcessInteractiveLine("if true {", ref buffer, ref suppress));
        Assert.NotNull(buffer);

        // Cancelled ReadLine (Ctrl+C) is signalled by a null input.
        var result = ShellInterpreter.ProcessInteractiveLine(null, ref buffer, ref suppress);

        Assert.Null(result);
        Assert.Null(buffer);
        Assert.False(suppress);
    }

    [Fact]
    public void ProcessInteractiveLine_NullInputWithEmptyBuffer_IsNoOp()
    {
        StringBuilder? buffer = null;
        var suppress = false;

        var result = ShellInterpreter.ProcessInteractiveLine(null, ref buffer, ref suppress);

        Assert.Null(result);
        Assert.Null(buffer);
        Assert.False(suppress);
    }

    [Fact]
    public void ProcessInteractiveLine_EmptyLine_ReturnsEmptyAndDoesNotStartBuffer()
    {
        StringBuilder? buffer = null;
        var suppress = false;

        var result = ShellInterpreter.ProcessInteractiveLine(string.Empty, ref buffer, ref suppress);

        Assert.Equal(string.Empty, result);
        Assert.Null(buffer);
        Assert.False(suppress);
    }

    [Fact]
    public void EncodeDecodeHistoryLine_MultiLineEntry_SurvivesDiskRoundTrip()
    {
        var original = new[]
        {
            "ls",
            "if true {\n    echo hello\n}",
            "echo \\n",
            "echo \"hello\nworld\"",
        };

        var path = Path.Combine(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(path, original.Select(ShellInterpreter.EncodeHistoryLine));
            var decoded = File.ReadAllLines(path).Select(ShellInterpreter.DecodeHistoryLine).ToArray();

            Assert.Equal(original, decoded);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void DecodeHistoryLine_PrefixedLineWithInvalidEscape_ReturnsRawLine()
    {
        // A pre-existing history entry that literally begins with the prefix
        // and contains a backslash sequence we never emit (\x). Decoding must
        // leave it untouched rather than mangling the user's data.
        var raw = "CosmosDBShellHistoryV1:hello \\x world";

        Assert.Equal(raw, ShellInterpreter.DecodeHistoryLine(raw));
    }

    [Fact]
    public void DecodeHistoryLine_PrefixedLineEndingInLoneBackslash_ReturnsRawLine()
    {
        var raw = "CosmosDBShellHistoryV1:trailing\\";

        Assert.Equal(raw, ShellInterpreter.DecodeHistoryLine(raw));
    }

    [Fact]
    public void DecodeHistoryLine_PrefixedLineWithOnlyValidEscapes_DecodesNormally()
    {
        var encoded = "CosmosDBShellHistoryV1:line1\\nline2\\\\end";

        Assert.Equal("line1\nline2\\end", ShellInterpreter.DecodeHistoryLine(encoded));
    }

    [Fact]
    public void IsIncompleteInput_IncompleteExpression_ReturnsTrue()
    {
        // Inputs that trail off mid-expression must be recognized as
        // incomplete so the REPL prompts for another line, regardless of
        // whether the unexpected-end is raised by the statement parser or
        // the expression parser. These cases exercise the expression-parser
        // AbortUnexpectedEnd path (without `ParseErrorKind.UnexpectedEnd`
        // they would all be misclassified as definitive syntax errors and
        // the REPL would execute them instead of prompting for more).
        Assert.True(ShellInterpreter.IsIncompleteInput("if (1 +"));
        Assert.True(ShellInterpreter.IsIncompleteInput("while (1 + 2"));
        Assert.True(ShellInterpreter.IsIncompleteInput("if a +"));
        Assert.True(ShellInterpreter.IsIncompleteInput("echo (1+"));
    }
}
