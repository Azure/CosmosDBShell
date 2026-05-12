// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

namespace CosmosShell.Tests.Parser;

public class ExpressionTests
{
    private Expression ParseExpression(string input)
    {
        var lexer = new Lexer(input);
        var parser = new ExpressionParser(lexer);
        return parser.ParseFilterExpression();
    }

    private async Task<ShellObject> EvaluateExpressionAsync(string input)
    {
        var expression = ParseExpression(input);
        return await expression.EvaluateAsync(ShellInterpreter.Instance, new CommandState(), CancellationToken.None);
    }

    private async Task<ShellObject> EvaluateExpressionWithJsonAsync(string input, object? value)
    {
        var expression = ParseExpression(input);
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(value)),
        };

        return await expression.EvaluateAsync(ShellInterpreter.Instance, state, CancellationToken.None);
    }

#pragma warning disable CS0618, VSTHRD002 // Type or member is obsolete, Synchronously waiting on tasks
    private ShellObject EvaluateExpression(string input)
    {
        var expression = ParseExpression(input);
        return expression.EvaluateAsync(ShellInterpreter.Instance, new CommandState(), CancellationToken.None).GetAwaiter().GetResult();
    }
#pragma warning restore CS0618, VSTHRD002

    private (Expression? Expr, Exception? Exception, int ErrorCount) TryParse(string input)
    {
        var lexer = new Lexer(input);
        var parser = new ExpressionParser(lexer);
        Expression? expr = null;
        Exception? ex = Record.Exception(() => expr = parser.ParseExpression());
        return (expr, ex, lexer.Errors.Count);
    }

    [Fact]
    public void ParseExpression_CommandExpressionPlainUrl_ParsesAsSingleShellWord()
    {
        var expr = ParseExpression("(connect https://localhost:9922)");
        var parens = Assert.IsType<ParensExpression>(expr);
        var command = Assert.IsType<CommandExpression>(parens.InnerExpression);

        Assert.Equal("connect", command.Name);
        Assert.Single(command.Arguments);
        Assert.IsNotType<CommandOption>(command.Arguments[0]);
        Assert.Equal("https://localhost:9922", command.Arguments[0].ToString());
    }

    [Fact]
    public void ParseExpression_CommandExpressionOptionWithUrl_ParsesAsSingleShellWord()
    {
        var expr = ParseExpression("(connect https://myaccount.documents.azure.com:443/ --authority-host=https://login.microsoftonline.us/)");
        var parens = Assert.IsType<ParensExpression>(expr);
        var command = Assert.IsType<CommandExpression>(parens.InnerExpression);

        Assert.Equal(2, command.Arguments.Count);
        Assert.Equal("https://myaccount.documents.azure.com:443/", command.Arguments[0].ToString());
        var option = Assert.IsType<CommandOption>(command.Arguments[1]);
        Assert.Equal("authority-host", option.Name);
        Assert.Equal("https://login.microsoftonline.us/", option.Value?.ToString());
    }

    [Fact]
    public void ParseExpression_CommandExpressionNegativeNumber_NotTreatedAsOption()
    {
        var expr = ParseExpression("(echo -5)");
        var parens = Assert.IsType<ParensExpression>(expr);
        var command = Assert.IsType<CommandExpression>(parens.InnerExpression);

        Assert.Single(command.Arguments);
        Assert.IsNotType<CommandOption>(command.Arguments[0]);
        Assert.Equal("-5", command.Arguments[0].ToString());
    }

    [Fact]
    public void ParseExpression_CommandExpressionOptionWithPaddedValue_ParsesAsSingleShellWord()
    {
        var expr = ParseExpression("(connect --key=abc==)");
        var parens = Assert.IsType<ParensExpression>(expr);
        var command = Assert.IsType<CommandExpression>(parens.InnerExpression);
        var option = Assert.Single(command.Arguments.OfType<CommandOption>());

        Assert.Equal("key", option.Name);
        Assert.Equal("abc==", option.Value?.ToString());
    }

    [Fact]
    public void ParseExpression_CommandExpressionCommaSeparatedValue_ParsesAsSingleShellWord()
    {
        var expr = ParseExpression("(echo red,green,blue)");
        var parens = Assert.IsType<ParensExpression>(expr);
        var command = Assert.IsType<CommandExpression>(parens.InnerExpression);

        Assert.Single(command.Arguments);
        Assert.Equal("red,green,blue", command.Arguments[0].ToString());
    }

    #region Constant Expression Tests

    [Fact]
    public void ParseExpression_Number_ReturnsConstantExpression()
    {
        var expr = ParseExpression("42");
        var constExpr = Assert.IsType<ConstantExpression>(expr);
        Assert.IsType<ShellNumber>(constExpr.Value);
        Assert.Equal(42, ((ShellNumber)constExpr.Value).Value);
    }

    [Fact]
    public void ParseExpression_Boolean_ReturnsConstantExpression()
    {
        var exprTrue = ParseExpression("true");
        var exprFalse = ParseExpression("false");

        var constTrue = Assert.IsType<ConstantExpression>(exprTrue);
        var constFalse = Assert.IsType<ConstantExpression>(exprFalse);

        Assert.IsType<ShellBool>(constTrue.Value);
        Assert.IsType<ShellBool>(constFalse.Value);

        Assert.True(((ShellBool)constTrue.Value).Value);
        Assert.False(((ShellBool)constFalse.Value).Value);
    }

    [Fact]
    public void ParseExpression_String_ReturnsConstantExpression()
    {
        var expr = ParseExpression("\"hello world\"");
        var constExpr = Assert.IsType<ConstantExpression>(expr);
        Assert.IsType<ShellText>(constExpr.Value);
        Assert.Equal("hello world", ((ShellText)constExpr.Value).Text);
    }

    [Fact]
    public void ParseExpression_Variable_ReturnsVariableExpression()
    {
        var expr = ParseExpression("$myVar");
        var varExpr = Assert.IsType<VariableExpression>(expr);
        Assert.Equal("myVar", varExpr.Name);
    }

    #endregion

    #region Binary Operator Expression Tests

    [Theory]
    [InlineData("2 + 3", 5)]
    [InlineData("10 - 4", 6)]
    [InlineData("3 * 4", 12)]
    [InlineData("15 / 3", 5)]
    [InlineData("17 % 5", 2)]
    public void EvaluateExpression_ArithmeticOperators_ReturnsCorrectResult(string expression, int expected)
    {
        var result = EvaluateExpression(expression);
        var num = Assert.IsType<ShellNumber>(result);
        Assert.Equal(expected, num.Value);
    }

    [Fact]
    public void EvaluateExpression_PowerOperator_ReturnsCorrectResult()
    {
        var result = EvaluateExpression("2 ** 3");
        var num = Assert.IsType<ShellNumber>(result);
        Assert.Equal(8, num.Value);
    }

    [Fact]
    public void EvaluateExpression_StringConcatenation_ReturnsCorrectResult()
    {
        var result = EvaluateExpression("\"hello\" + \" world\"");
        var txt = Assert.IsType<ShellText>(result);
        Assert.Equal("hello world", txt.Text);
    }

    [Theory]
    [InlineData("5 == 5", true)]
    [InlineData("5 == 3", false)]
    [InlineData("5 != 3", true)]
    [InlineData("5 != 5", false)]
    [InlineData("5 < 10", true)]
    [InlineData("10 < 5", false)]
    [InlineData("10 > 5", true)]
    [InlineData("5 > 10", false)]
    [InlineData("5 <= 5", true)]
    [InlineData("5 <= 3", false)]
    [InlineData("5 >= 5", true)]
    [InlineData("7 >= 10", false)]
    public void EvaluateExpression_ComparisonOperators_ReturnsCorrectResult(string expression, bool expected)
    {
        var result = EvaluateExpression(expression);
        var b = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, b.Value);
    }

    [Theory]
    [InlineData("true && true", true)]
    [InlineData("true && false", false)]
    [InlineData("false && true", false)]
    [InlineData("false && false", false)]
    [InlineData("true || true", true)]
    [InlineData("true || false", true)]
    [InlineData("false || true", true)]
    [InlineData("false || false", false)]
    [InlineData("true ^ true", false)]
    [InlineData("true ^ false", true)]
    [InlineData("false ^ true", true)]
    [InlineData("false ^ false", false)]
    public void EvaluateExpression_LogicalOperators_ReturnsCorrectResult(string expression, bool expected)
    {
        var result = EvaluateExpression(expression);
        var b = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, b.Value);
    }

    [Fact]
    public void EvaluateExpression_DivisionByZero_ThrowsException()
        => Assert.Throws<DivideByZeroException>(() => EvaluateExpression("10 / 0"));

    [Fact]
    public void EvaluateExpression_ModuloByZero_ThrowsException()
        => Assert.Throws<DivideByZeroException>(() => EvaluateExpression("10 % 0"));

    #endregion

    #region Unary Operator Expression Tests

    [Theory]
    [InlineData("!true", false)]
    [InlineData("!false", true)]
    public void EvaluateExpression_LogicalNot_ReturnsCorrectResult(string expression, bool expected)
    {
        var result = EvaluateExpression(expression);
        var b = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, b.Value);
    }

    [Theory]
    [InlineData("-5", -5)]
    [InlineData("-(-5)", 5)]
    [InlineData("+5", 5)]
    [InlineData("+(+5)", 5)]
    public void EvaluateExpression_UnaryPlusMinusOperators_ReturnsCorrectResult(string expression, int expected)
    {
        var result = EvaluateExpression(expression);
        var num = Assert.IsType<ShellNumber>(result);
        Assert.Equal(expected, num.Value);
    }

    #endregion

    #region Precedence Tests

    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("2 * 3 + 4", 10)]
    [InlineData("10 - 2 * 3", 4)]
    [InlineData("20 / 4 + 3", 8)]
    public void EvaluateExpression_OperatorPrecedence_ReturnsCorrectResult(string expression, int expected)
    {
        var result = EvaluateExpression(expression);
        var num = Assert.IsType<ShellNumber>(result);
        Assert.Equal(expected, num.Value);
    }

    [Fact]
    public void EvaluateExpression_PowerOperatorRightAssociative_ReturnsCorrectResult()
    {
        var result = EvaluateExpression("2 ** 3 ** 2");
        var num = Assert.IsType<ShellNumber>(result);
        Assert.Equal(512, num.Value);
    }

    [Theory]
    [InlineData("true || false && false", true)]
    [InlineData("false && true || true", true)]
    public void EvaluateExpression_LogicalOperatorPrecedence_ReturnsCorrectResult(string expression, bool expected)
    {
        var result = EvaluateExpression(expression);
        var b = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, b.Value);
    }

    [Theory]
    [InlineData("2 + 3 > 4", true)]
    [InlineData("2 < 3 + 4", true)]
    [InlineData("1 + 1 == 2", true)]
    public void EvaluateExpression_ComparisonWithArithmetic_ReturnsCorrectResult(string expression, bool expected)
    {
        var result = EvaluateExpression(expression);
        var b = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, b.Value);
    }

    #endregion

    #region Parentheses Tests

    [Theory]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("2 * (3 + 4)", 14)]
    [InlineData("((2 + 3) * 4)", 20)]
    [InlineData("(2 + (3 * 4))", 14)]
    public void EvaluateExpression_Parentheses_ReturnsCorrectResult(string expression, int expected)
    {
        var result = EvaluateExpression(expression);
        var num = Assert.IsType<ShellNumber>(result);
        Assert.Equal(expected, num.Value);
    }

    #endregion

    #region Complex Expression Tests

    [Fact]
    public void EvaluateExpression_ComplexArithmetic_ReturnsCorrectResult()
    {
        var result = EvaluateExpression("(10 + 5) * 2 - 8 / 4 + 3 ** 2");
        var num = Assert.IsType<ShellNumber>(result);
        Assert.Equal(37, num.Value);
    }

    [Fact]
    public void EvaluateExpression_ComplexBoolean_ReturnsCorrectResult()
    {
        var result = EvaluateExpression("true && (false || true) && !(false && true)");
        var b = Assert.IsType<ShellBool>(result);
        Assert.True(b.Value);
    }

    [Fact]
    public void EvaluateExpression_MixedTypes_StringConcatenation_ReturnsCorrectResult()
    {
        var result = EvaluateExpression("\"The answer is \" + 42");
        var txt = Assert.IsType<ShellText>(result);
        Assert.Equal("The answer is 42", txt.Text);
    }

    #endregion

    #region Type Conversion Tests

    [Fact]
    public void ShellObject_BooleanToNumber_ConvertsCorrectly()
    {
        var trueAsNumber = new ShellBool(true).ConvertShellObject(DataType.Number);
        var falseAsNumber = new ShellBool(false).ConvertShellObject(DataType.Number);
        Assert.Equal(1, trueAsNumber);
        Assert.Equal(0, falseAsNumber);
    }

    [Fact]
    public void ShellObject_NumberToBoolean_ConvertsCorrectly()
    {
        Assert.False((bool)new ShellNumber(0).ConvertShellObject(DataType.Boolean));
        Assert.True((bool)new ShellNumber(5).ConvertShellObject(DataType.Boolean));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public void ShellObject_StringToBoolean_ConvertsCorrectly(string value, bool expected)
    {
        var result = new ShellText(value).ConvertShellObject(DataType.Boolean);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Error Handling Tests (updated to use error list instead of Assert.Throws)

    [Fact]
    public void ParseExpression_InvalidSyntax_ReportsError()
    {
        var r = TryParse("2 +");
        Assert.True(r.Exception != null || r.ErrorCount > 0, "Expected a parse error for '2 +'.");
    }

    [Fact]
    public void ParseExpression_MismatchedParentheses_ReportsError()
    {
        var r = TryParse("(2 + 3");
        Assert.True(r.Exception != null || r.ErrorCount > 0, "Expected a parse error for '(2 + 3'.");
    }

    [Fact]
    public void ParseExpression_EmptyExpression_ReportsError()
    {
        var r = TryParse("");
        Assert.True(r.Exception != null || r.ErrorCount > 0, "Expected a parse error for empty input.");
    }

    [Fact]
    public void ParseExpression_MalformedJson_ReportsError()
    {
        var r = TryParse("{ \"name\": \"John\"");
        Assert.True(r.Exception != null || r.ErrorCount > 0, "Expected a parse error for malformed JSON missing closing brace.");
    }

    [Fact]
    public void ParseExpression_InvalidJsonSyntax_ReportsError()
    {
        var r = TryParse("{ \"name\": }");
        Assert.True(r.Exception != null || r.ErrorCount > 0, "Expected a parse error for invalid JSON syntax.");
    }

    #endregion

    #region Short Circuit Evaluation Tests

    [Fact]
    public void EvaluateExpression_AndOperator_ShortCircuits()
    {
        var result = EvaluateExpression("false && (1 / 0 == 0)");
        var b = Assert.IsType<ShellBool>(result);
        Assert.False(b.Value);
    }

    [Fact]
    public void EvaluateExpression_OrOperator_ShortCircuits()
    {
        var result = EvaluateExpression("true || (1 / 0 == 0)");
        var b = Assert.IsType<ShellBool>(result);
        Assert.True(b.Value);
    }

    #endregion

    #region JSON Expression Tests

    [Fact]
    public void ParseExpression_SimpleJsonObject_ReturnsJsonExpression()
    {
        var expr = ParseExpression("{ \"name\": \"John\", \"age\": 30 }");
        Assert.IsType<JsonExpression>(expr);
    }

    [Fact]
    public void EvaluateExpression_JsonObject_ReturnsShellJson()
    {
        var result = EvaluateExpression("{ \"name\": \"John\", \"age\": 30 }");
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, json.Value.ValueKind);
        Assert.Equal("John", json.Value.GetProperty("name").GetString());
        Assert.Equal(30, json.Value.GetProperty("age").GetInt32());
    }

    [Fact]
    public void EvaluateExpression_JsonArray_ReturnsShellJson()
    {
        var result = EvaluateExpression("[1, 2, 3, 4, 5]");
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal(5, json.Value.GetArrayLength());
        Assert.Equal(1, json.Value[0].GetInt32());
        Assert.Equal(5, json.Value[4].GetInt32());
    }

    [Fact]
    public void ParseExpression_EmptyJsonArray_ReturnsJsonExpression()
    {
        var result = EvaluateExpression("[]");
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal(0, json.Value.GetArrayLength());
    }

    #endregion

    #region JSON Path Expression Tests

    [Fact]
    public void ParseExpression_JsonPathArrayAccess_ReturnsJsonPathExpression()
    {
        var expr = ParseExpression("$.[0]");
        var path = Assert.IsType<JSonPathExpression>(expr);
        Assert.Equal(".[0]", path.JSonPath);
    }

    [Fact]
    public void ParseExpression_JsonPathPropertyAccess_ReturnsJsonPathExpression()
    {
        var expr = ParseExpression("$.name");
        var path = Assert.IsType<JSonPathExpression>(expr);
        Assert.Equal(".name", path.JSonPath);
    }

    [Fact]
    public void ParseExpression_JsonPathNestedAccess_ReturnsJsonPathExpression()
    {
        var expr = ParseExpression("$.users[0].name");
        var path = Assert.IsType<JSonPathExpression>(expr);
        Assert.Equal(".users[0].name", path.JSonPath);
    }

    [Fact]
    public void ParseExpression_VariablePropertyAccess_ReturnsJsonPathExpression()
    {
        var expr = ParseExpression("$script.name");
        var path = Assert.IsType<JSonPathExpression>(expr);
        Assert.Equal("script.name", path.JSonPath);
    }

    [Fact]
    public async Task EvaluateExpression_VariablePropertyAccess_ReturnsPropertyValue()
    {
        var interpreter = new ShellInterpreter();
        var scriptJson = new ShellJson(System.Text.Json.JsonSerializer.SerializeToElement(new { name = "foo.csh", path = "C:\\tmp\\foo.csh" }));
        interpreter.SetVariable("script", scriptJson);

        var result = await ParseExpression("$script.name").EvaluateAsync(interpreter, new CommandState(), CancellationToken.None);
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal("foo.csh", json.Value.GetString());
    }

    [Fact]
    public void ParseExpression_VariableArrayAccess_ReturnsJsonPathExpression()
    {
        var expr = ParseExpression("$items[0]");
        var path = Assert.IsType<JSonPathExpression>(expr);
        Assert.Equal("items[0]", path.JSonPath);
    }

    [Fact]
    public void ParseExpression_FilterRootPath_ReturnsFilterPathExpression()
    {
        var expr = ParseExpression(".items[0].id");
        var path = Assert.IsType<FilterPathExpression>(expr);
        Assert.Equal(3, path.Segments.Count);
        Assert.IsType<FilterPropertySegment>(path.Segments[0]);
        Assert.IsType<FilterIndexSegment>(path.Segments[1]);
        Assert.IsType<FilterPropertySegment>(path.Segments[2]);
    }

    [Fact]
    public void ParseExpression_FilterQuotedPropertyPath_ReturnsFilterPathExpression()
    {
        var expr = ParseExpression(".[\"Volcano Name\"]");
        var path = Assert.IsType<FilterPathExpression>(expr);
        var segment = Assert.IsType<FilterPropertySegment>(Assert.Single(path.Segments));
        Assert.Equal("Volcano Name", segment.Name);
    }

    [Fact]
    public void ParseExpression_FilterBuiltinCall_ReturnsFilterCallExpression()
    {
        var expr = ParseExpression("map(.id)");
        var call = Assert.IsType<FilterCallExpression>(expr);
        Assert.Equal("map", call.Name);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void ParseExpression_FilterPipe_ReturnsFilterPipeExpression()
    {
        var expr = ParseExpression(".items | length");
        var pipe = Assert.IsType<FilterPipeExpression>(expr);
        Assert.IsType<FilterPathExpression>(pipe.Left);
        Assert.IsType<FilterCallExpression>(pipe.Right);
    }

    [Fact]
    public void ParseExpression_ObjectShorthand_ReturnsJsonExpression()
    {
        var expr = ParseExpression("{id, status}");
        var json = Assert.IsType<JsonExpression>(expr);
        Assert.Equal(2, json.Properties.Count);
        Assert.All(json.Properties.Values, value => Assert.IsType<FilterPathExpression>(value));
    }

    [Fact]
    public async Task EvaluateExpression_FilterRootPath_ReturnsPropertyValue()
    {
        var result = await EvaluateExpressionWithJsonAsync(".items[0].id", new { items = new[] { new { id = "1" } } });
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal("1", json.Value.GetString());
    }

    [Fact]
    public async Task EvaluateExpression_FilterQuotedPropertyPath_ReturnsPropertyValue()
    {
        var result = await EvaluateExpressionWithJsonAsync(".[\"Volcano Name\"]", new Dictionary<string, object?> { ["Volcano Name"] = "Abu" });
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal("Abu", json.Value.GetString());
    }

    [Fact]
    public async Task EvaluateExpression_FilterDotQuotedPropertyPath_ReturnsPropertyValue()
    {
        var result = await EvaluateExpressionWithJsonAsync(".\"Volcano Name\"", new Dictionary<string, object?> { ["Volcano Name"] = "Abu" });
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal("Abu", json.Value.GetString());
    }

    [Fact]
    public async Task EvaluateExpression_FilterMapProjection_WithQuotedPropertyPath_ReturnsProjectedArray()
    {
        var result = await EvaluateExpressionWithJsonAsync(
            ".items | map({\"Volcano Name\": .[\"Volcano Name\"], Country})",
            new
            {
                items = new[]
                {
                    new Dictionary<string, object?> { ["Volcano Name"] = "Abu", ["Country"] = "Japan", ["Region"] = "Honshu-Japan" },
                    new Dictionary<string, object?> { ["Volcano Name"] = "Acamarachi", ["Country"] = "Chile", ["Region"] = "Chile-N" },
                },
            });

        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal("Abu", json.Value[0].GetProperty("Volcano Name").GetString());
        Assert.Equal("Japan", json.Value[0].GetProperty("Country").GetString());
        Assert.False(json.Value[0].TryGetProperty("Region", out _));
        Assert.Equal("Acamarachi", json.Value[1].GetProperty("Volcano Name").GetString());
        Assert.Equal("Chile", json.Value[1].GetProperty("Country").GetString());
    }

    [Fact]
    public async Task EvaluateExpression_FilterPipeLength_ReturnsCount()
    {
        var result = await EvaluateExpressionWithJsonAsync(".items | length", new { items = new[] { 1, 2, 3 } });
        var number = Assert.IsType<ShellNumber>(result);
        Assert.Equal(3, number.Value);
    }

    [Fact]
    public async Task EvaluateExpression_FilterMap_ReturnsProjectedArray()
    {
        var result = await EvaluateExpressionWithJsonAsync(".items | map(.id)", new { items = new[] { new { id = "b" }, new { id = "a" } } });
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal("b", json.Value[0].GetString());
        Assert.Equal("a", json.Value[1].GetString());
    }

    [Fact]
    public async Task EvaluateExpression_FilterMapType_ReturnsStringArray()
    {
        var result = await EvaluateExpressionWithJsonAsync(".items | map(type)", new { items = new object?[] { "active", 42, true } });
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal("string", json.Value[0].GetString());
        Assert.Equal("number", json.Value[1].GetString());
        Assert.Equal("boolean", json.Value[2].GetString());
    }

    [Fact]
    public async Task EvaluateExpression_FilterContains_WithStringLiteral_ReturnsTrue()
    {
        var result = await EvaluateExpressionWithJsonAsync(".tags | contains(\"prod\")", new { tags = new[] { "dev", "prod" } });
        var shellBool = Assert.IsType<ShellBool>(result);
        Assert.True(shellBool.Value);
    }

    [Fact]
    public async Task EvaluateExpression_FilterContains_WithBooleanLiteral_ReturnsTrue()
    {
        var result = await EvaluateExpressionWithJsonAsync(".flags | contains(true)", new { flags = new[] { false, true } });
        var shellBool = Assert.IsType<ShellBool>(result);
        Assert.True(shellBool.Value);
    }

    [Theory]
    [InlineData("length(.items)")]
    [InlineData("keys(.items)")]
    [InlineData("type(.items)")]
    public async Task EvaluateExpression_FilterZeroArgumentBuiltin_WithArgument_Throws(string expression)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => EvaluateExpressionWithJsonAsync(expression, new { items = new[] { 1, 2, 3 } }));
    }

    [Fact]
    public async Task EvaluateExpression_FilterSelect_ReturnsFilteredArray()
    {
        var result = await EvaluateExpressionWithJsonAsync(
            ".items | select(.status == \"active\")",
            new { items = new[] { new { id = "1", status = "active" }, new { id = "2", status = "inactive" } } });

        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(1, json.Value.GetArrayLength());
        Assert.Equal("1", json.Value[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task EvaluateExpression_FilterSortBy_ReturnsSortedArray()
    {
        var result = await EvaluateExpressionWithJsonAsync(
            ".items | sort_by(.id)",
            new { items = new[] { new { id = "b" }, new { id = "a" } } });

        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal("a", json.Value[0].GetProperty("id").GetString());
        Assert.Equal("b", json.Value[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task EvaluateExpression_FilterIterationPipe_ReturnsSequenceAsJsonArray()
    {
        var result = await EvaluateExpressionWithJsonAsync(
            ".items[] | .id",
            new { items = new[] { new { id = "1" }, new { id = "2" } } });

        var sequence = Assert.IsType<ShellSequence>(result);
        Assert.Equal(2, sequence.Elements.Count);
        Assert.Equal("1", sequence.Elements[0].GetString());
        Assert.Equal("2", sequence.Elements[1].GetString());
    }

    #endregion

    #region Decimal Expression Tests

    [Fact]
    public void ParseExpression_Decimal_ReturnsConstantExpression()
    {
        var expr = ParseExpression("3.14");
        var constExpr = Assert.IsType<ConstantExpression>(expr);
        Assert.IsType<ShellDecimal>(constExpr.Value);
        Assert.Equal(3.14, ((ShellDecimal)constExpr.Value).Value);
    }

    [Theory]
    [InlineData("3.14", 3.14)]
    [InlineData("0.5", 0.5)]
    [InlineData("123.456", 123.456)]
    [InlineData("1.23e4", 12300.0)]
    [InlineData("1.23E-2", 0.0123)]
    [InlineData("5e3", 5000.0)]
    public void ParseExpression_VariousDecimalFormats_ReturnsCorrectValue(string expression, double expected)
    {
        var result = EvaluateExpression(expression);
        var dec = Assert.IsType<ShellDecimal>(result);
        Assert.Equal(expected, dec.Value);
    }

    [Theory]
    [InlineData("2.5 + 3.5", 6.0)]
    [InlineData("10.0 - 4.5", 5.5)]
    [InlineData("3.0 * 4.5", 13.5)]
    [InlineData("15.0 / 3.0", 5.0)]
    [InlineData("17.5 % 5.0", 2.5)]
    public void EvaluateExpression_DecimalArithmeticOperators_ReturnsCorrectResult(string expression, double expected)
    {
        var result = EvaluateExpression(expression);
        var dec = Assert.IsType<ShellDecimal>(result);
        Assert.Equal(expected, dec.Value, 10);
    }

    [Fact]
    public void EvaluateExpression_DecimalPowerOperator_ReturnsCorrectResult()
    {
        var result = EvaluateExpression("2.0 ** 3.0");
        var dec = Assert.IsType<ShellDecimal>(result);
        Assert.Equal(8.0, dec.Value);
    }

    [Theory]
    [InlineData("2.5 + 3", 5.5)]
    [InlineData("5 + 2.5", 7.5)]
    [InlineData("10 - 3.5", 6.5)]
    [InlineData("4.0 * 3", 12.0)]
    [InlineData("15 / 2.0", 7.5)]
    public void EvaluateExpression_MixedDecimalAndInteger_ReturnsDecimalResult(string expression, double expected)
    {
        var result = EvaluateExpression(expression);
        var dec = Assert.IsType<ShellDecimal>(result);
        Assert.Equal(expected, dec.Value, 10);
    }

    [Theory]
    [InlineData("3.14 == 3.14", true)]
    [InlineData("3.14 == 3.15", false)]
    [InlineData("3.14 != 3.15", true)]
    [InlineData("3.14 != 3.14", false)]
    [InlineData("3.14 < 3.15", true)]
    [InlineData("3.15 < 3.14", false)]
    [InlineData("3.15 > 3.14", true)]
    [InlineData("3.14 > 3.15", false)]
    [InlineData("3.14 <= 3.14", true)]
    [InlineData("3.14 <= 3.13", false)]
    [InlineData("3.14 >= 3.14", true)]
    [InlineData("3.15 >= 3.16", false)]
    public void EvaluateExpression_DecimalComparisonOperators_ReturnsCorrectResult(string expression, bool expected)
    {
        var result = EvaluateExpression(expression);
        var b = Assert.IsType<ShellBool>(result);
        Assert.Equal(expected, b.Value);
    }

    [Theory]
    [InlineData("-3.14", -3.14)]
    [InlineData("-(-3.14)", 3.14)]
    [InlineData("+3.14", 3.14)]
    [InlineData("+(+3.14)", 3.14)]
    public void EvaluateExpression_UnaryOperatorsOnDecimals_ReturnsCorrectResult(string expression, double expected)
    {
        var result = EvaluateExpression(expression);
        var dec = Assert.IsType<ShellDecimal>(result);
        Assert.Equal(expected, dec.Value);
    }

    [Fact]
    public void EvaluateExpression_DecimalDivisionByZero_ThrowsException()
        => Assert.Throws<DivideByZeroException>(() => EvaluateExpression("10.0 / 0.0"));

    [Fact]
    public void EvaluateExpression_DecimalModuloByZero_ThrowsException()
        => Assert.Throws<DivideByZeroException>(() => EvaluateExpression("10.5 % 0.0"));

    [Theory]
    [InlineData("2.5 + 3.5 * 4.0", 16.5)]
    [InlineData("(2.5 + 3.5) * 4.0", 24.0)]
    [InlineData("10.0 - 2.5 * 2.0", 5.0)]
    public void EvaluateExpression_DecimalOperatorPrecedence_ReturnsCorrectResult(string expression, double expected)
    {
        var result = EvaluateExpression(expression);
        var dec = Assert.IsType<ShellDecimal>(result);
        Assert.Equal(expected, dec.Value, 10);
    }

    [Fact]
    public void EvaluateExpression_DecimalScientificNotation_ReturnsCorrectResult()
    {
        var result = EvaluateExpression("1.5e2 + 2.5e1");
        var dec = Assert.IsType<ShellDecimal>(result);
        Assert.Equal(175.0, dec.Value);
    }

    [Fact]
    public void ShellObject_DecimalToBoolean_ConvertsCorrectly()
    {
        Assert.False((bool)new ShellDecimal(0.0).ConvertShellObject(DataType.Boolean));
        Assert.True((bool)new ShellDecimal(3.14).ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public void ShellObject_DecimalToNumber_TruncatesCorrectly()
    {
        Assert.Equal(3, new ShellDecimal(3.14).ConvertShellObject(DataType.Number));
        Assert.Equal(3, new ShellDecimal(3.99).ConvertShellObject(DataType.Number));
    }

    [Theory]
    [InlineData("3.14", "3.14")]
    [InlineData("3.0", "3")]
    [InlineData("0.5", "0.5")]
    public void ShellObject_DecimalToText_FormatsCorrectly(string expression, string expected)
    {
        var result = EvaluateExpression(expression);
        var text = result.ConvertShellObject(DataType.Text);
        Assert.Equal(expected, text);
    }

    #endregion
}