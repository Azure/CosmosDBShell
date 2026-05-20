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
}
