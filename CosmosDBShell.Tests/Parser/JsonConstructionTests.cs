// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Exercises JSON object construction (<see cref="JsonExpression"/>) and array
/// construction (<see cref="JsonArrayExpression"/>) across every value DataType branch
/// (Json, Number, Decimal, Boolean, Text, null) plus filter-sequence flattening and
/// shorthand property capture from the current result.
/// </summary>
public class JsonConstructionTests
{
    private static async Task<JsonElement> EvalJsonAsync(string input, object? value = null)
    {
        var expr = new ExpressionParser(new Lexer(input)).ParseFilterExpression();
        var state = new CommandState();
        if (value != null)
        {
            state.Result = new ShellJson(JsonSerializer.SerializeToElement(value));
        }

        var result = await expr.EvaluateAsync(ShellInterpreter.Instance, state, CancellationToken.None);
        return Assert.IsType<ShellJson>(result).Value;
    }

    [Fact]
    public async Task Array_WithNumbers_BuildsJsonArray()
    {
        var json = await EvalJsonAsync("[1, 2, 3]");
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(new[] { 1, 2, 3 }, json.EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public async Task Array_WithDecimals_BuildsJsonArray()
    {
        var json = await EvalJsonAsync("[1.5, 2.5]");
        Assert.Equal(new[] { 1.5, 2.5 }, json.EnumerateArray().Select(e => e.GetDouble()));
    }

    [Fact]
    public async Task Array_WithStrings_BuildsJsonArray()
    {
        var json = await EvalJsonAsync("[\"a\", \"b\"]");
        Assert.Equal(new[] { "a", "b" }, json.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Array_WithBooleans_BuildsJsonArray()
    {
        var json = await EvalJsonAsync("[true, false]");
        Assert.Equal(new[] { JsonValueKind.True, JsonValueKind.False }, json.EnumerateArray().Select(e => e.ValueKind));
    }

    [Fact]
    public async Task Array_WithNestedArray_BuildsNestedJson()
    {
        var json = await EvalJsonAsync("[[1, 2], [3]]");
        Assert.Equal(2, json.GetArrayLength());
        Assert.Equal(JsonValueKind.Array, json[0].ValueKind);
    }

    [Fact]
    public async Task Array_Empty_BuildsEmptyJsonArray()
    {
        var json = await EvalJsonAsync("[]");
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(0, json.GetArrayLength());
    }

    [Fact]
    public async Task Array_FlattensFilterSequence()
    {
        var json = await EvalJsonAsync("[.items[]]", new { items = new[] { 10, 20, 30 } });
        Assert.Equal(new[] { 10, 20, 30 }, json.EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public async Task Object_WithMixedValueTypes_BuildsJsonObject()
    {
        var json = await EvalJsonAsync("{ n: 1, d: 2.5, s: \"hi\", b: true }");
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.Equal(1, json.GetProperty("n").GetInt32());
        Assert.Equal(2.5, json.GetProperty("d").GetDouble());
        Assert.Equal("hi", json.GetProperty("s").GetString());
        Assert.Equal(JsonValueKind.True, json.GetProperty("b").ValueKind);
    }

    [Fact]
    public async Task Object_WithNullValue_SerializesNull()
    {
        var json = await EvalJsonAsync("{ x: null }");
        Assert.Equal(JsonValueKind.Null, json.GetProperty("x").ValueKind);
    }

    [Fact]
    public async Task Object_WithNestedObject_BuildsNestedJson()
    {
        var json = await EvalJsonAsync("{ outer: { inner: 1 } }");
        Assert.Equal(1, json.GetProperty("outer").GetProperty("inner").GetInt32());
    }

    [Fact]
    public async Task Object_ShorthandProperties_CaptureFromCurrentResult()
    {
        var json = await EvalJsonAsync("{ id, status }", new { id = "1", status = "active", extra = "ignored" });
        Assert.Equal("1", json.GetProperty("id").GetString());
        Assert.Equal("active", json.GetProperty("status").GetString());
        Assert.False(json.TryGetProperty("extra", out _));
    }

    [Fact]
    public async Task Object_PropertyFromJsonPath_BuildsJsonObject()
    {
        var json = await EvalJsonAsync("{ name: .title }", new { title = "Volcano" });
        Assert.Equal("Volcano", json.GetProperty("name").GetString());
    }
}
