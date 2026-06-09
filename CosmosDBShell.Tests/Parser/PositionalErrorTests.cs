// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System;

using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Direct unit tests for <see cref="PositionalErrorHelper.GetLineAndColumn"/> (line/column
/// resolution from a flat character offset) and the <see cref="PositionalException"/>
/// carrier used to surface script-relative error positions.
/// </summary>
public class PositionalErrorTests
{
    [Fact]
    public void GetLineAndColumn_FirstCharacter_IsLineOneColumnOne()
    {
        var (line, column, lineText) = PositionalErrorHelper.GetLineAndColumn("hello world", 0);
        Assert.Equal(1, line);
        Assert.Equal(1, column);
        Assert.Equal("hello world", lineText);
    }

    [Fact]
    public void GetLineAndColumn_MidFirstLine_TracksColumn()
    {
        var (line, column, _) = PositionalErrorHelper.GetLineAndColumn("hello world", 6);
        Assert.Equal(1, line);
        Assert.Equal(7, column);
    }

    [Fact]
    public void GetLineAndColumn_SecondLine_TracksLineAndColumn()
    {
        var text = "first\nsecond\nthird";
        var position = text.IndexOf("second", StringComparison.Ordinal) + 2;
        var (line, column, lineText) = PositionalErrorHelper.GetLineAndColumn(text, position);
        Assert.Equal(2, line);
        Assert.Equal(3, column);
        Assert.Equal("second", lineText);
    }

    [Fact]
    public void GetLineAndColumn_LastLineWithoutTrailingNewline_ReturnsRemainder()
    {
        var text = "a\nb\nlast line";
        var (line, _, lineText) = PositionalErrorHelper.GetLineAndColumn(text, text.Length);
        Assert.Equal(3, line);
        Assert.Equal("last line", lineText);
    }

    [Fact]
    public void GetLineAndColumn_TrimsCarriageReturn()
    {
        var text = "windows\r\nline";
        var (line, _, lineText) = PositionalErrorHelper.GetLineAndColumn(text, 0);
        Assert.Equal(1, line);
        Assert.Equal("windows", lineText);
    }

    [Fact]
    public void GetLineAndColumn_PositionBeyondLength_ClampsToEnd()
    {
        var text = "short";
        var (line, _, lineText) = PositionalErrorHelper.GetLineAndColumn(text, 1000);
        Assert.Equal(1, line);
        Assert.Equal("short", lineText);
    }

    [Fact]
    public void PositionalException_ExposesPositionAndInnerMessage()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new PositionalException("script.csh", inner, line: 4, column: 7, lineText: "echo bad");

        Assert.Equal("script.csh", ex.FileName);
        Assert.Equal(4, ex.Line);
        Assert.Equal(7, ex.Column);
        Assert.Equal("echo bad", ex.LineText);
        Assert.Equal("boom", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void PositionalException_AllowsNullLineText()
    {
        var ex = new PositionalException("f.csh", new Exception("x"), 1, 1);
        Assert.Null(ex.LineText);
    }
}
