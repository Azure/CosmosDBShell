// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Exercises <see cref="ShellObject.Parse(Lexer)"/>, which maps the first lexer token
/// to a concrete <see cref="ShellObject"/> subtype (boolean, number, identifier, text).
/// </summary>
public class ShellObjectParseTests
{
    private static ShellObject Parse(string input) => ShellObject.Parse(new Lexer(input));

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    public void Parse_TrueLiteral_ReturnsBoolTrue(string input)
    {
        var result = Parse(input);
        Assert.True(Assert.IsType<ShellBool>(result).Value);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("False")]
    public void Parse_FalseLiteral_ReturnsBoolFalse(string input)
    {
        var result = Parse(input);
        Assert.False(Assert.IsType<ShellBool>(result).Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    public void Parse_NumericToken_ReturnsIdentifier(string input)
    {
        // Numeric input is lexed as a Number token, which falls through to the
        // default branch and is wrapped as an identifier carrying the raw text.
        var result = Parse(input);
        Assert.Equal(input, Assert.IsType<ShellIdentifier>(result).Value);
    }

    [Fact]
    public void Parse_BareWord_ReturnsIdentifier()
    {
        var result = Parse("hello");
        Assert.Equal("hello", Assert.IsType<ShellIdentifier>(result).Value);
    }

    [Fact]
    public void Parse_QuotedString_ReturnsText()
    {
        var result = Parse("\"quoted value\"");
        Assert.Equal("quoted value", Assert.IsType<ShellText>(result).Text);
    }
}
