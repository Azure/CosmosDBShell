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
/// Exercises <see cref="FilterPathExpression"/> navigation: property access, indexing,
/// iteration (<c>[]</c>), optional segments (<c>?</c>), missing-property null fallback,
/// out-of-range indexing, and the type-mismatch error branches for each segment kind.
/// </summary>
public class FilterPathExpressionTests
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

    [Fact]
    public async Task Property_ExistingKey_ReturnsValue()
    {
        var result = await EvalAsync(".name", new { name = "abu" });
        Assert.Equal("abu", Assert.IsType<ShellJson>(result).Value.GetString());
    }

    [Fact]
    public async Task Property_MissingKey_ReturnsNull()
    {
        var result = await EvalAsync(".missing", new { name = "abu" });
        Assert.Equal(JsonValueKind.Null, Assert.IsType<ShellJson>(result).Value.ValueKind);
    }

    [Fact]
    public async Task Property_OnNonObjectWithoutOptional_Throws()
        => await Assert.ThrowsAsync<CommandException>(() => EvalAsync(".name", 5));

    [Fact]
    public async Task Property_OnNonObjectWithOptional_ReturnsNull()
    {
        var result = await EvalAsync(".name?", 5);
        Assert.Equal(JsonValueKind.Null, Assert.IsType<ShellJson>(result).Value.ValueKind);
    }

    [Fact]
    public async Task Index_InRange_ReturnsElement()
    {
        var result = await EvalAsync(".[1]", new[] { 10, 20, 30 });
        Assert.Equal(20, Assert.IsType<ShellJson>(result).Value.GetInt32());
    }

    [Fact]
    public async Task Index_OutOfRange_ReturnsNull()
    {
        var result = await EvalAsync(".[9]", new[] { 10, 20, 30 });
        Assert.Equal(JsonValueKind.Null, Assert.IsType<ShellJson>(result).Value.ValueKind);
    }

    [Fact]
    public async Task Index_OnNonArrayWithoutOptional_Throws()
        => await Assert.ThrowsAsync<CommandException>(() => EvalAsync(".[0]", new { a = 1 }));

    [Fact]
    public async Task Iterate_OverArray_ReturnsSequence()
    {
        var result = await EvalAsync(".items[]", new { items = new[] { 1, 2, 3 } });
        var sequence = Assert.IsType<ShellSequence>(result);
        Assert.Equal(3, sequence.Elements.Count);
    }

    [Fact]
    public async Task Iterate_OnNonArrayWithoutOptional_Throws()
        => await Assert.ThrowsAsync<CommandException>(() => EvalAsync(".[]", new { a = 1 }));

    [Fact]
    public async Task NestedPath_PropertyThenIndex_ReturnsElement()
    {
        var result = await EvalAsync(".items[0].id", new { items = new[] { new { id = "x" } } });
        Assert.Equal("x", Assert.IsType<ShellJson>(result).Value.GetString());
    }

    [Fact]
    public async Task Property_OnStringScalarWithOptional_ReturnsNull()
    {
        // A string scalar is not an object; the optional marker suppresses the type error.
        var result = await EvalAsync(".name?", "scalar");
        Assert.Equal(JsonValueKind.Null, Assert.IsType<ShellJson>(result).Value.ValueKind);
    }
}
