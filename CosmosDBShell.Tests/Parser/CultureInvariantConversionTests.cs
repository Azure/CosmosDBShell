// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class CultureInvariantConversionTests
{
    [Fact]
    public void ShellText_DecimalConversion_UsesInvariantCulture()
    {
        var value = WithGermanCulture(() =>
            Assert.IsType<double>(new ShellText("1.5").ConvertShellObject(DataType.Decimal)));

        Assert.Equal(1.5d, value);
    }

    [Fact]
    public void ShellText_DecimalConversion_RejectsLocalizedDecimalSeparator()
    {
        WithGermanCulture(() =>
            Assert.Throws<InvalidOperationException>(() => new ShellText("1,5").ConvertShellObject(DataType.Decimal)));
    }

    [Fact]
    public void ShellIdentifier_DecimalConversion_UsesInvariantCulture()
    {
        var value = WithGermanCulture(() =>
            Assert.IsType<double>(new ShellIdentifier("1.5").ConvertShellObject(DataType.Decimal)));

        Assert.Equal(1.5d, value);

        WithGermanCulture(() =>
            Assert.Throws<InvalidOperationException>(() => new ShellIdentifier("1,5").ConvertShellObject(DataType.Decimal)));
    }

    [Fact]
    public async Task ExpressionEvaluation_TextDecimalArithmetic_UsesInvariantCulture()
    {
        var result = await WithGermanCultureAsync(() => EvaluateExpressionAsync("\"1.5\" * 2.0"));
        var value = Assert.IsType<ShellDecimal>(result);

        Assert.Equal(3.0d, value.Value);
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("\"1.5\"", 1.5)]
    [InlineData("1e100", 1e100)]
    [InlineData("\"1e100\"", 1e100)]
    public void ShellJson_DecimalConversion_ReturnsInvariantDouble(string source, double expected)
    {
        using var document = JsonDocument.Parse(source);
        var value = WithGermanCulture(() =>
            Assert.IsType<double>(new ShellJson(document.RootElement).ConvertShellObject(DataType.Decimal)));

        Assert.Equal(expected, value);
    }

    [Fact]
    public void ShellJson_DecimalConversion_RejectsLocalizedDecimalSeparator()
    {
        using var document = JsonDocument.Parse("\"1,5\"");

        WithGermanCulture(() =>
            Assert.Throws<InvalidOperationException>(() =>
                new ShellJson(document.RootElement).ConvertShellObject(DataType.Decimal)));
    }

    private static T WithGermanCulture<T>(Func<T> action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    private static async Task<T> WithGermanCultureAsync<T>(Func<Task<T>> action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return await action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    private static async Task<ShellObject> EvaluateExpressionAsync(string input)
    {
        var expression = new ExpressionParser(new Lexer(input)).ParseFilterExpression();
        using var shell = ShellInterpreter.CreateInstance();
        return await expression.EvaluateAsync(shell, new CommandState(), CancellationToken.None);
    }
}
