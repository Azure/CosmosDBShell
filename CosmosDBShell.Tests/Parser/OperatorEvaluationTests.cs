// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Evaluates binary and unary operator expressions to exercise the arithmetic,
/// comparison, logical, and type-coercion branches in
/// <see cref="BinaryOperatorExpression"/> and <see cref="UnaryOperatorExpression"/>.
/// </summary>
public class OperatorEvaluationTests
{
    private static async Task<ShellObject> EvalAsync(string input)
    {
        var expression = new ExpressionParser(new Lexer(input)).ParseFilterExpression();
        return await expression.EvaluateAsync(ShellInterpreter.Instance, new CommandState(), CancellationToken.None);
    }

    [Theory]
    [InlineData("1 + 2", 3)]
    [InlineData("5 - 3", 2)]
    [InlineData("4 * 3", 12)]
    [InlineData("10 / 2", 5)]
    [InlineData("10 % 3", 1)]
    [InlineData("2 ** 3", 8)]
    public async Task IntegerArithmetic_ReturnsExpectedNumber(string input, int expected)
    {
        var result = await EvalAsync(input);
        var number = Assert.IsType<ShellNumber>(result);
        Assert.Equal(expected, number.Value);
    }

    [Theory]
    [InlineData("1.5 + 2.5", 4.0)]
    [InlineData("5.0 - 2.5", 2.5)]
    [InlineData("2.5 * 2.0", 5.0)]
    [InlineData("5.0 / 2.0", 2.5)]
    [InlineData("5.5 % 2.0", 1.5)]
    [InlineData("2.0 ** 3.0", 8.0)]
    public async Task DecimalArithmetic_ReturnsExpectedDecimal(string input, double expected)
    {
        var result = await EvalAsync(input);
        var dec = Assert.IsType<ShellDecimal>(result);
        Assert.Equal(expected, dec.Value);
    }

    [Theory]
    [InlineData("\"a\" + \"b\"", "ab")]
    [InlineData("\"n=\" + 5", "n=5")]
    [InlineData("5 + \"px\"", "5px")]
    public async Task StringConcatenation_ReturnsText(string input, string expected)
    {
        var result = await EvalAsync(input);
        var text = Assert.IsType<ShellText>(result);
        Assert.Equal(expected, text.Text);
    }

    [Fact]
    public async Task ArrayConcatenation_MergesJsonArrays()
    {
        var result = await EvalAsync("[1, 2] + [3]");
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal(3, json.Value.GetArrayLength());
    }

    [Theory]
    [InlineData("1 == 1", true)]
    [InlineData("1 == 2", false)]
    [InlineData("1 != 2", true)]
    [InlineData("1 != 1", false)]
    [InlineData("1 < 2", true)]
    [InlineData("2 < 1", false)]
    [InlineData("2 > 1", true)]
    [InlineData("1 <= 1", true)]
    [InlineData("2 <= 1", false)]
    [InlineData("2 >= 2", true)]
    [InlineData("2 >= 3", false)]
    public async Task IntegerComparison_ReturnsExpectedBool(string input, bool expected)
    {
        var result = await EvalAsync(input);
        var boolean = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, boolean.Value);
    }

    [Theory]
    [InlineData("1.5 == 1.5", true)]
    [InlineData("1.5 < 2.5", true)]
    [InlineData("2.5 > 1.5", true)]
    [InlineData("2.5 <= 2.5", true)]
    [InlineData("2.5 >= 3.5", false)]
    public async Task DecimalComparison_ReturnsExpectedBool(string input, bool expected)
    {
        var result = await EvalAsync(input);
        var boolean = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, boolean.Value);
    }

    [Theory]
    [InlineData("true == true", true)]
    [InlineData("true == false", false)]
    [InlineData("\"a\" == \"a\"", true)]
    [InlineData("\"a\" == \"b\"", false)]
    [InlineData("1 == \"1\"", true)]
    public async Task EqualityAcrossTypes_ReturnsExpectedBool(string input, bool expected)
    {
        var result = await EvalAsync(input);
        var boolean = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, boolean.Value);
    }

    [Theory]
    [InlineData("true && true", true)]
    [InlineData("true && false", false)]
    [InlineData("false || true", true)]
    [InlineData("false || false", false)]
    [InlineData("true ^ false", true)]
    [InlineData("true ^ true", false)]
    public async Task LogicalOperators_ReturnExpectedBool(string input, bool expected)
    {
        var result = await EvalAsync(input);
        var boolean = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, boolean.Value);
    }

    [Fact]
    public async Task LogicalAnd_ShortCircuits_DoesNotEvaluateRight()
    {
        // The right side would divide by zero; short-circuit must avoid evaluating it.
        var result = await EvalAsync("false && (10 / 0 == 0)");
        Assert.False(Assert.IsType<ShellBool>(result).Value);
    }

    [Fact]
    public async Task LogicalOr_ShortCircuits_DoesNotEvaluateRight()
    {
        var result = await EvalAsync("true || (10 / 0 == 0)");
        Assert.True(Assert.IsType<ShellBool>(result).Value);
    }

    [Theory]
    [InlineData("10 / 0")]
    [InlineData("10.0 / 0.0")]
    [InlineData("10 % 0")]
    [InlineData("10.0 % 0.0")]
    public async Task DivisionOrModuloByZero_Throws(string input)
    {
        await Assert.ThrowsAsync<DivideByZeroException>(() => EvalAsync(input));
    }

    [Theory]
    [InlineData("!true", false)]
    [InlineData("!false", true)]
    public async Task UnaryNot_NegatesBoolean(string input, bool expected)
    {
        var result = await EvalAsync(input);
        Assert.Equal(expected, Assert.IsType<ShellBool>(result).Value);
    }

    [Theory]
    [InlineData("-5", -5)]
    [InlineData("+5", 5)]
    public async Task UnaryNumeric_ReturnsNumber(string input, int expected)
    {
        var result = await EvalAsync(input);
        Assert.Equal(expected, Assert.IsType<ShellNumber>(result).Value);
    }

    [Theory]
    [InlineData("-2.5", -2.5)]
    [InlineData("+2.5", 2.5)]
    public async Task UnaryNumeric_ReturnsDecimal(string input, double expected)
    {
        var result = await EvalAsync(input);
        Assert.Equal(expected, Assert.IsType<ShellDecimal>(result).Value);
    }
}
