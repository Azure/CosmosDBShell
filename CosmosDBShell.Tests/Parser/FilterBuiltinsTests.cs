// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class FilterBuiltinsTests
{
    private static async Task<ShellObject> EvalAsync(string input, string inputJson)
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var document = JsonDocument.Parse(inputJson);
        var expr = new ExpressionParser(new Lexer(input)).ParseFilterExpression();
        var state = new CommandState { Result = new ShellJson(document.RootElement.Clone()) };

        return await expr.EvaluateAsync(shell, state, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Map_PreservesIntegralDecimalThroughPipeline()
    {
        var result = await EvalAsync(". | map(3.0) | .[0]", "[1]");
        var json = FilterExpressionUtilities.ToJsonElement(result);
        Assert.Equal("3.0", json.GetRawText());
        Assert.IsType<ShellDecimal>(ShellNumber.FromJson(json));
    }

    [Theory]
    [InlineData(". | type", "{\"a\":1}", typeof(ShellText))]
    [InlineData(". | type", "[1,2]", typeof(ShellText))]
    [InlineData(". | type", "\"hello\"", typeof(ShellText))]
    [InlineData(". | type", "42", typeof(ShellText))]
    [InlineData(". | type", "true", typeof(ShellText))]
    [InlineData(". | type", "null", typeof(ShellText))]
    [InlineData(". | length", "{\"a\":1,\"b\":2,\"c\":3}", typeof(ShellNumber))]
    [InlineData(". | length", "\"abcd\"", typeof(ShellNumber))]
    [InlineData(". | length", "null", typeof(ShellNumber))]
    [InlineData(". | keys", "{\"banana\":1,\"apple\":2,\"cherry\":3}", typeof(ShellJson))]
    [InlineData(". | contains(2)", "[1,2,3]", typeof(ShellBool))]
    [InlineData(". | contains(9)", "[1,2,3]", typeof(ShellBool))]
    [InlineData(". | contains(\"ell\")", "\"hello\"", typeof(ShellBool))]
    [InlineData(". | contains(5)", "5", typeof(ShellBool))]
    [InlineData(". | contains({id: 1})", "{\"id\":1,\"name\":\"x\"}", typeof(ShellBool))]
    [InlineData(". | contains({id: 2})", "[{\"id\":1},{\"id\":2}]", typeof(ShellBool))]
    [InlineData(". | contains([3,4])", "[[1,2],[3,4]]", typeof(ShellBool))]
    [InlineData(". | select(. > 2)", "[1,2,3,4]", typeof(ShellJson))]
    [InlineData(". | sort_by(.)", "[3,1,2]", typeof(ShellJson))]
    [InlineData(". | sort_by(.)", "[\"cherry\",\"apple\",\"banana\"]", typeof(ShellJson))]
    [InlineData(". | sort_by(.)", "[3,\"a\",true,null]", typeof(ShellJson))]
    [InlineData(". | sort_by(.k)", "[{\"k\":[2]},{\"k\":[1]}]", typeof(ShellJson))]
    public async Task Builtins_PreserveInternalResultTypes(string expression, string inputJson, Type expectedType)
    {
        var result = await EvalAsync(expression, inputJson);

        Assert.IsType(expectedType, result);
    }

    [Theory]
    [InlineData(". | length", "5")]
    [InlineData(". | keys", "[1,2]")]
    [InlineData(". | select(. > 2)", "5")]
    [InlineData(". | sort_by(.)", "5")]
    [InlineData(". | bogus_function(.)", "[1]")]
    public async Task InvalidInput_ThrowsCommandExceptionBeforeCommandWrapping(string expression, string inputJson)
    {
        await Assert.ThrowsAsync<CommandException>(() => EvalAsync(expression, inputJson));
    }
}
