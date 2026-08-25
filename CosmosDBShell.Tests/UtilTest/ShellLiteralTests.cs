// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

using Xunit;

/// <summary>
/// Pins the contract that <see cref="ShellLiteral.Quote"/> and the lexer are inverse:
/// parsing a quoted value must yield exactly that value again.
/// </summary>
public class ShellLiteralTests
{
    private static bool RoundTrips(string value)
    {
        var expression = new ExpressionParser(new Lexer(ShellLiteral.Quote(value))).ParseExpression();

        return expression is ConstantExpression constant
            && constant.Value is ShellText text
            && text.Text == value;
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("\\")]
    [InlineData("a\\")]
    [InlineData("\\\\")]
    [InlineData("\"")]
    [InlineData("\\\"")]
    // A literal backslash followed by a unicode escape must not be decoded on re-parse.
    [InlineData("\\u0041")]
    [InlineData("$name")]
    [InlineData("$(echo injected)")]
    [InlineData("${x}")]
    [InlineData("foo; echo injected")]
    [InlineData("left|right")]
    [InlineData("a\u001Bb")]
    public void Quote_RoundTripsAdversarialSequences(string value)
    {
        Assert.True(RoundTrips(value), value);
    }

    [Fact]
    public void Quote_RoundTripsEveryBmpCharacter()
    {
        var failures = new List<string>();

        for (int code = 0; code <= 0xFFFF; code++)
        {
            var value = "a" + (char)code + "b";
            if (!RoundTrips(value))
            {
                failures.Add($"U+{code:X4}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(", ", failures.Take(20)));
    }

    [Fact]
    public void Quote_RoundTripsRandomCombinations()
    {
        const string alphabet = "\\\"'$(){}[]; \t\n\r\u0000\u001Bu0041ab";
        var random = new Random(20260825);
        var failures = new List<string>();
        var builder = new StringBuilder();

        for (int i = 0; i < 5000; i++)
        {
            builder.Clear();
            var length = random.Next(0, 12);
            for (int j = 0; j < length; j++)
            {
                builder.Append(alphabet[random.Next(alphabet.Length)]);
            }

            var value = builder.ToString();
            if (!RoundTrips(value))
            {
                failures.Add(value);
            }
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures.Take(10)));
    }
}
