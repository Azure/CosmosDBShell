// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class FilterPathExpressionTests
{
    private static async Task<ShellObject> EvalAsync(string input, string inputJson)
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var document = JsonDocument.Parse(inputJson);
        var expr = new ExpressionParser(new Lexer(input)).ParseFilterExpression();
        var state = new CommandState { Result = new ShellJson(document.RootElement.Clone()) };

        return await expr.EvaluateAsync(shell, state, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(".name", "{\"name\":\"abu\"}")]
    [InlineData(".missing", "{\"name\":\"abu\"}")]
    [InlineData(".name?", "5")]
    [InlineData(".name?", "\"scalar\"")]
    [InlineData(".[1]", "[10,20,30]")]
    [InlineData(".[9]", "[10,20,30]")]
    [InlineData(".[2147483647]", "[10,20,30]")]
    [InlineData(".[2147483647]?", "[10,20,30]")]
    [InlineData(".items[0].id", "{\"items\":[{\"id\":\"x\"}]}")]
    public async Task Paths_PreserveShellJsonResultType(string expression, string inputJson)
    {
        var result = await EvalAsync(expression, inputJson);

        Assert.IsType<ShellJson>(result);
    }

    [Theory]
    [InlineData(".name", "5")]
    [InlineData(".[0]", "{\"a\":1}")]
    [InlineData(".[]", "{\"a\":1}")]
    public async Task InvalidPath_ThrowsCommandExceptionBeforeCommandWrapping(string expression, string inputJson)
    {
        await Assert.ThrowsAsync<CommandException>(() => EvalAsync(expression, inputJson));
    }

    [Theory]
    [InlineData(".[2147483648]", "2147483648")]
    [InlineData(".[2147483648]?", "2147483648")]
    [InlineData(".items[2147483648]?", "2147483648")]
    [InlineData(".items[999999999999999999999999]", "999999999999999999999999")]
    [InlineData(".[999999999999999999999999999999]", "999999999999999999999999999999")]
    public void Index_Overflow_ReportsErrorAtIndex(string expression, string index)
    {
        var lexer = new Lexer(expression);
        var parser = new ExpressionParser(lexer);

        var parsedExpression = parser.ParseFilterExpression();

        Assert.IsType<ErrorExpression>(parsedExpression);
        Assert.True(lexer.Errors.HasErrors);
        var error = Assert.Single(lexer.Errors);
        Assert.Equal(expression.IndexOf(index, StringComparison.Ordinal), error.Start);
        Assert.Equal(index.Length, error.Length);
        Assert.Equal(
            Azure.Data.Cosmos.Shell.Util.MessageService.GetArgsString("expression_error_invalid_number", "value", index),
            error.Message);
    }

    [Fact]
    public async Task Iterate_OverArray_ReturnsSequence()
    {
        var result = await EvalAsync(".items[]", "{\"items\":[1,2,3]}");
        var sequence = Assert.IsType<ShellSequence>(result);
        Assert.Equal(3, sequence.Elements.Count);
    }
}
