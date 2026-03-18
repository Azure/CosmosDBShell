// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

namespace CosmosShell.Tests.Parser;

public class JSonArrayExpressionTests
{
    private static Expression ParseExpression(string input)
    {
        var lexer = new Lexer(input);
        var parser = new ExpressionParser(lexer);
        return parser.ParseExpression();
    }

    [Fact]
    public void ParseEmptyArray_ReturnsJSonArrayExpressionWithNoElements()
    {
        // Arrange & Act
        var expr = ParseExpression("[]");

        // Assert
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        Assert.Empty(arrayExpr.Expressions);
        Assert.Equal(TokenType.OpenBracket, arrayExpr.LBracketToken.Type);
        Assert.Equal(TokenType.CloseBracket, arrayExpr.RBracketToken.Type);
    }


    [Fact]
    public void ParseSingleVariableArray()
    {
        var expr = ParseExpression("[$i]");

        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
    }

    [Fact]
    public void ParseArrayWithNumbers_ReturnsJSonArrayExpressionWithNumberElements()
    {
        // Arrange & Act
        var expr = ParseExpression("[1, 2, 3]");

        // Assert
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        Assert.Equal(3, arrayExpr.Expressions.Count);

        for (int i = 0; i < 3; i++)
        {
            var constExpr = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[i]);
            var number = Assert.IsType<ShellNumber>(constExpr.Value);
            Assert.Equal(i + 1, number.Value);
        }
    }

    [Fact]
    public void ParseArrayWithStrings_ReturnsJSonArrayExpressionWithStringElements()
    {
        // Arrange & Act
        var expr = ParseExpression("[\"hello\", \"world\"]");

        // Assert
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        Assert.Equal(2, arrayExpr.Expressions.Count);

        var const0 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[0]);
        var text0 = Assert.IsType<ShellText>(const0.Value);
        Assert.Equal("hello", text0.Text);

        var const1 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[1]);
        var text1 = Assert.IsType<ShellText>(const1.Value);
        Assert.Equal("world", text1.Text);
    }

    [Fact]
    public void ParseArrayWithBooleans_ReturnsJSonArrayExpressionWithBooleanElements()
    {
        // Arrange & Act
        var expr = ParseExpression("[true, false, true]");

        // Assert
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        Assert.Equal(3, arrayExpr.Expressions.Count);

        var const0 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[0]);
        var bool0 = Assert.IsType<ShellBool>(const0.Value);
        Assert.True(bool0.Value);

        var const1 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[1]);
        var bool1 = Assert.IsType<ShellBool>(const1.Value);
        Assert.False(bool1.Value);

        var const2 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[2]);
        var bool2 = Assert.IsType<ShellBool>(const2.Value);
        Assert.True(bool2.Value);
    }

    [Fact]
    public void ParseArrayWithExpressions_ReturnsJSonArrayExpressionWithExpressionElements()
    {
        // Arrange & Act
        var expr = ParseExpression("[1 + 2, 3 * 4, 5 - 1]");

        // Assert
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        Assert.Equal(3, arrayExpr.Expressions.Count);

        Assert.IsType<BinaryOperatorExpression>(arrayExpr.Expressions[0]);
        Assert.IsType<BinaryOperatorExpression>(arrayExpr.Expressions[1]);
        Assert.IsType<BinaryOperatorExpression>(arrayExpr.Expressions[2]);
    }

    [Fact]
    public void ParseNestedArrays_ReturnsJSonArrayExpressionWithNestedArrays()
    {
        // Arrange & Act
        var expr = ParseExpression("[[1, 2], [3, 4], []]");

        // Assert
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        Assert.Equal(3, arrayExpr.Expressions.Count);

        var nested0 = Assert.IsType<JsonArrayExpression>(arrayExpr.Expressions[0]);
        Assert.Equal(2, nested0.Expressions.Count);

        var nested1 = Assert.IsType<JsonArrayExpression>(arrayExpr.Expressions[1]);
        Assert.Equal(2, nested1.Expressions.Count);

        var nested2 = Assert.IsType<JsonArrayExpression>(arrayExpr.Expressions[2]);
        Assert.Empty(nested2.Expressions);
    }

    [Fact]
    public void ParseArrayWithMixedTypes_ReturnsJSonArrayExpressionWithMixedElements()
    {
        // Arrange & Act
        var expr = ParseExpression("[1, \"text\", true, null, [1, 2], 2]");

        // Assert
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        Assert.Equal(6, arrayExpr.Expressions.Count);

        // Number
        var const0 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[0]);
        Assert.IsType<ShellNumber>(const0.Value);

        // String
        var const1 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[1]);
        Assert.IsType<ShellText>(const1.Value);

        // Boolean
        var const2 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[2]);
        Assert.IsType<ShellBool>(const2.Value);

        // Null (might be parsed as identifier "null")
        var const3 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[3]);

        // Nested array
        Assert.IsType<JsonArrayExpression>(arrayExpr.Expressions[4]);

        // Object
        var const4 = Assert.IsType<ConstantExpression>(arrayExpr.Expressions[5]);
        Assert.IsType<ShellNumber>(const4.Value);
    }

    [Fact]
    public void ParseArrayWithLineBreaks_HandlesWhitespaceCorrectly()
    {
        // Arrange & Act
        var expr = ParseExpression(@"[
            1,
            2,
            3
        ]");

        // Assert
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        Assert.Equal(3, arrayExpr.Expressions.Count);
    }

    [Fact]
    public async Task EvaluateJSonArrayExpression_ReturnsShellJsonWithCorrectValues()
    {
        // Arrange
        var expr = ParseExpression("[1 + 1, \"hello\", true]");
        var arrayExpr = Assert.IsType<JsonArrayExpression>(expr);
        var interpreter = new ShellInterpreter();
        var state = new CommandState();

        // Act
        var result = await arrayExpr.EvaluateAsync(interpreter, state, CancellationToken.None);

        // Assert
        var shellJson = Assert.IsType<ShellJson>(result);
        Assert.Equal(JsonValueKind.Array, shellJson.Value.ValueKind);

        var elements = shellJson.Value.EnumerateArray().ToArray();
        Assert.Equal(3, elements.Length);
        Assert.Equal(2, elements[0].GetInt32());
        Assert.Equal("hello", elements[1].GetString());
        Assert.True(elements[2].GetBoolean());
    }
}