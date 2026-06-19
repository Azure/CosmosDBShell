// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Exercises the filter built-in functions (<c>type</c>, <c>length</c>, <c>keys</c>,
/// <c>contains</c>, <c>select</c>, <c>sort_by</c>) and the supporting helpers in
/// <see cref="FilterExpressionUtilities"/> (DescribeKind, Contains, Compare, JsonEquals)
/// across JSON value kinds and their error branches.
/// </summary>
public class FilterBuiltinsTests
{
    private static async Task<ShellObject> EvalAsync(string input, object? value)
    {
        var expr = new ExpressionParser(new Lexer(input)).ParseFilterExpression();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(value)),
        };

        return await expr.EvaluateAsync(ShellInterpreter.Instance, state, CancellationToken.None);
    }

    private static string TypeName(ShellObject result) => Assert.IsType<ShellText>(result).Text;

    [Fact]
    public async Task Type_Object_ReturnsObject()
        => Assert.Equal("object", TypeName(await EvalAsync(". | type", new { a = 1 })));

    [Fact]
    public async Task Type_Array_ReturnsArray()
        => Assert.Equal("array", TypeName(await EvalAsync(". | type", new[] { 1, 2 })));

    [Fact]
    public async Task Type_String_ReturnsString()
        => Assert.Equal("string", TypeName(await EvalAsync(". | type", "hello")));

    [Fact]
    public async Task Type_Number_ReturnsNumber()
        => Assert.Equal("number", TypeName(await EvalAsync(". | type", 42)));

    [Fact]
    public async Task Type_Boolean_ReturnsBoolean()
        => Assert.Equal("boolean", TypeName(await EvalAsync(". | type", true)));

    [Fact]
    public async Task Type_Null_ReturnsNull()
        => Assert.Equal("null", TypeName(await EvalAsync(". | type", (object?)null)));

    [Fact]
    public async Task Length_Object_CountsProperties()
    {
        var result = await EvalAsync(". | length", new { a = 1, b = 2, c = 3 });
        Assert.Equal(3, Assert.IsType<ShellNumber>(result).Value);
    }

    [Fact]
    public async Task Length_String_CountsCharacters()
    {
        var result = await EvalAsync(". | length", "abcd");
        Assert.Equal(4, Assert.IsType<ShellNumber>(result).Value);
    }

    [Fact]
    public async Task Length_Null_ReturnsZero()
    {
        var result = await EvalAsync(". | length", (object?)null);
        Assert.Equal(0, Assert.IsType<ShellNumber>(result).Value);
    }

    [Fact]
    public async Task Length_Number_Throws()
        => await Assert.ThrowsAsync<CommandException>(() => EvalAsync(". | length", 5));

    [Fact]
    public async Task Keys_Object_ReturnsSortedKeys()
    {
        var result = await EvalAsync(". | keys", new { banana = 1, apple = 2, cherry = 3 });
        var json = Assert.IsType<ShellJson>(result);
        var keys = json.Value.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "apple", "banana", "cherry" }, keys);
    }

    [Fact]
    public async Task Keys_Array_Throws()
        => await Assert.ThrowsAsync<CommandException>(() => EvalAsync(". | keys", new[] { 1, 2 }));

    [Fact]
    public async Task Contains_NumberInArray_ReturnsTrue()
        => Assert.True(Assert.IsType<ShellBool>(await EvalAsync(". | contains(2)", new[] { 1, 2, 3 })).Value);

    [Fact]
    public async Task Contains_NumberNotInArray_ReturnsFalse()
        => Assert.False(Assert.IsType<ShellBool>(await EvalAsync(". | contains(9)", new[] { 1, 2, 3 })).Value);

    [Fact]
    public async Task Contains_Substring_ReturnsTrue()
        => Assert.True(Assert.IsType<ShellBool>(await EvalAsync(". | contains(\"ell\")", "hello")).Value);

    [Fact]
    public async Task Contains_ScalarEquality_ReturnsTrue()
        => Assert.True(Assert.IsType<ShellBool>(await EvalAsync(". | contains(5)", 5)).Value);

    [Fact]
    public async Task Select_FiltersArrayByPredicate()
    {
        var result = await EvalAsync(". | select(. > 2)", new[] { 1, 2, 3, 4 });
        var json = Assert.IsType<ShellJson>(result);
        var values = json.Value.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal(new[] { 3, 4 }, values);
    }

    [Fact]
    public async Task Select_NonArray_Throws()
        => await Assert.ThrowsAsync<CommandException>(() => EvalAsync(". | select(. > 2)", 5));

    [Fact]
    public async Task SortBy_Numbers_SortsAscending()
    {
        var result = await EvalAsync(". | sort_by(.)", new[] { 3, 1, 2 });
        var json = Assert.IsType<ShellJson>(result);
        var values = json.Value.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, values);
    }

    [Fact]
    public async Task SortBy_Strings_SortsOrdinally()
    {
        var result = await EvalAsync(". | sort_by(.)", new[] { "cherry", "apple", "banana" });
        var json = Assert.IsType<ShellJson>(result);
        var values = json.Value.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "apple", "banana", "cherry" }, values);
    }

    [Fact]
    public async Task SortBy_MixedKinds_DoesNotThrow()
    {
        // Sorting heterogeneous keys exercises the cross-kind ranking branch in Compare.
        var result = await EvalAsync(". | sort_by(.)", new object?[] { 3, "a", true, null });
        var json = Assert.IsType<ShellJson>(result);
        Assert.Equal(4, json.Value.GetArrayLength());
    }

    [Fact]
    public async Task SortBy_NonArray_Throws()
        => await Assert.ThrowsAsync<CommandException>(() => EvalAsync(". | sort_by(.)", 5));

    [Fact]
    public async Task UnknownBuiltin_Throws()
        => await Assert.ThrowsAsync<CommandException>(() => EvalAsync(". | bogus_function(.)", new[] { 1 }));

    [Fact]
    public async Task Contains_ObjectSubset_ReturnsTrue()
    {
        // Object-in-object containment exercises the recursive ObjectEquals/Contains branch.
        var result = await EvalAsync(
            ". | contains({ id: 1 })",
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "x" });
        Assert.True(Assert.IsType<ShellBool>(result).Value);
    }

    [Fact]
    public async Task Contains_ObjectInArray_MatchesByValue()
    {
        // Matching an object element walks JsonEquals over objects via JsonElementComparer.
        var items = new object[]
        {
            new Dictionary<string, object?> { ["id"] = 1 },
            new Dictionary<string, object?> { ["id"] = 2 },
        };
        var result = await EvalAsync(". | contains({ id: 2 })", items);
        Assert.True(Assert.IsType<ShellBool>(result).Value);
    }

    [Fact]
    public async Task SortBy_ArrayKeys_OrdersByRawText()
    {
        // Array keys fall through Compare's raw-text branch without throwing.
        var rows = new object[]
        {
            new Dictionary<string, object?> { ["k"] = new[] { 2 } },
            new Dictionary<string, object?> { ["k"] = new[] { 1 } },
        };
        var result = await EvalAsync(". | sort_by(.k)", rows);
        Assert.Equal(2, Assert.IsType<ShellJson>(result).Value.GetArrayLength());
    }

    [Fact]
    public async Task ArrayEquality_ViaContains_UsesSequenceComparer()
    {
        // Containment of an array element compares arrays element-by-element.
        var items = new object[] { new[] { 1, 2 }, new[] { 3, 4 } };
        var result = await EvalAsync(". | contains([3, 4])", items);
        Assert.True(Assert.IsType<ShellBool>(result).Value);
    }
}
