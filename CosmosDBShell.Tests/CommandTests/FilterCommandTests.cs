// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class FilterCommandTests
{
    [Fact]
    public async Task ExecuteAsync_AppliesPathExpression_AndPreservesStructuredResult()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                items = new[]
                {
                    new { id = "1", status = "active" },
                    new { id = "2", status = "inactive" },
                },
            })),
        };

        var command = new FilterCommand
        {
            ExpressionText = ".items[0].id",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        Assert.Same(state, result);
        Assert.False(result.IsPrinted);
        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal("1", json.Value.GetString());
    }

    [Fact]
    public async Task ExecuteAsync_AppliesMapProjection()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                items = new[]
                {
                    new { id = "1", status = "active" },
                    new { id = "2", status = "inactive" },
                },
            })),
        };

        var command = new FilterCommand
        {
            ExpressionText = ".items | map({id, status})",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal("1", json.Value[0].GetProperty("id").GetString());
        Assert.Equal("active", json.Value[0].GetProperty("status").GetString());
        Assert.Equal("2", json.Value[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesSequenceResult_ToJsonArray()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                items = new[]
                {
                    new { id = "1" },
                    new { id = "2" },
                },
            })),
        };

        var command = new FilterCommand
        {
            ExpressionText = ".items[] | .id",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal("1", json.Value[0].GetString());
        Assert.Equal("2", json.Value[1].GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ArrayCollectorFlattensIterationSequence()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                items = new[]
                {
                    new { id = "1" },
                    new { id = "2" },
                    new { id = "3" },
                },
            })),
        };

        var command = new FilterCommand
        {
            ExpressionText = "[.items[] | .id]",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal(3, json.Value.GetArrayLength());
        Assert.Equal("1", json.Value[0].GetString());
        Assert.Equal("2", json.Value[1].GetString());
        Assert.Equal("3", json.Value[2].GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesTextResult_ToJsonString()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = "1" })),
        };

        var command = new FilterCommand
        {
            ExpressionText = "type",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.String, json.Value.ValueKind);
        Assert.Equal("object", json.Value.GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesBooleanResult_ToJsonBoolean()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = "1" })),
        };

        var command = new FilterCommand
        {
            ExpressionText = "true",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.True, json.Value.ValueKind);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenInputMissing()
    {
        var shell = ShellInterpreter.CreateInstance();
        var command = new FilterCommand
        {
            ExpressionText = ".items",
        };

        var ex = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, new CommandState(), string.Empty, CancellationToken.None));
        Assert.Contains("requires piped JSON input", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenExpressionInvalid()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { items = new[] { 1, 2 } })),
        };
        var command = new FilterCommand
        {
            ExpressionText = ".items[",
        };

        await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_SortBy_IsStableForEqualKeys()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                items = new[]
                {
                    new { id = "a", rank = 1 },
                    new { id = "b", rank = 1 },
                    new { id = "c", rank = 1 },
                    new { id = "d", rank = 1 },
                },
            })),
        };

        var command = new FilterCommand
        {
            ExpressionText = ".items | sort_by(.rank)",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.Array, json.Value.ValueKind);
        Assert.Equal("a", json.Value[0].GetProperty("id").GetString());
        Assert.Equal("b", json.Value[1].GetProperty("id").GetString());
        Assert.Equal("c", json.Value[2].GetProperty("id").GetString());
        Assert.Equal("d", json.Value[3].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_SortBy_OrdersByKey()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                items = new[]
                {
                    new { id = "a", rank = 3 },
                    new { id = "b", rank = 1 },
                    new { id = "c", rank = 2 },
                },
            })),
        };

        var command = new FilterCommand
        {
            ExpressionText = ".items | sort_by(.rank)",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal("b", json.Value[0].GetProperty("id").GetString());
        Assert.Equal("c", json.Value[1].GetProperty("id").GetString());
        Assert.Equal("a", json.Value[2].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOnTrailingTokens()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = "1" })),
        };

        var command = new FilterCommand
        {
            ExpressionText = ".id )",
        };

        var ex = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None));
        Assert.Contains("Unexpected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ParenthesizedPath_EvaluatesAsExpression()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = "1", status = "active" })),
        };

        var command = new FilterCommand
        {
            ExpressionText = "(.id)",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.String, json.Value.ValueKind);
        Assert.Equal("1", json.Value.GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ParenthesizedComparison_EvaluatesAsBoolean()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = 1 })),
        };

        var command = new FilterCommand
        {
            ExpressionText = "(.id == 1)",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.True, json.Value.ValueKind);
    }

    [Fact]
    public async Task ExecuteAsync_ParenthesizedExpression_InPipeline()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = "abc" })),
        };

        var command = new FilterCommand
        {
            ExpressionText = ". | (.id)",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal("abc", json.Value.GetString());
    }

    [Theory]
    [InlineData("length", "5", "length supports")]
    [InlineData("keys", "[1, 2]", "keys requires an object")]
    [InlineData("map(.id)", "{ \"id\": 1 }", "map requires an array")]
    [InlineData("select(.id)", "{ \"id\": 1 }", "select requires an array")]
    [InlineData("sort_by(.id)", "{ \"id\": 1 }", "sort_by requires an array")]
    public async Task ExecuteAsync_RuntimeTypeError_ThrowsLocalizedCommandException(string expression, string inputJson, string expectedFragment)
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonDocument.Parse(inputJson).RootElement.Clone()),
        };

        var command = new FilterCommand
        {
            ExpressionText = expression,
        };

        var ex = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None));
        Assert.Contains(expectedFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_PropertyAccessOnNonObject_ThrowsLocalizedCommandException()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(5)),
        };

        var command = new FilterCommand
        {
            ExpressionText = ".id",
        };

        var ex = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None));
        Assert.Contains("Cannot read property", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_OptionalPropertyOnNonObject_ReturnsNull()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(5)),
        };

        var command = new FilterCommand
        {
            ExpressionText = ".id?",
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        Assert.Equal(JsonValueKind.Null, json.Value.ValueKind);
    }

    [Fact]
    public async Task ExecuteAsync_SortBy_HandlesNumbersOutsideDecimalRange()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonDocument.Parse("[{\"v\": 1e308}, {\"v\": -1e308}, {\"v\": 0}]").RootElement.Clone()),
        };

        // Sort by a key that exceeds decimal range; must not throw FormatException.
        var sortCommand = new FilterCommand
        {
            ExpressionText = "sort_by(.v) | map(.v)",
        };

        var result = await sortCommand.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var json = Assert.IsType<ShellJson>(result.Result);
        var values = json.Value.EnumerateArray().Select(static e => e.GetDouble()).ToArray();
        Assert.Equal(new[] { -1e308, 0d, 1e308 }, values);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsCancellation()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonDocument.Parse("[1, 2, 3, 4, 5]").RootElement.Clone()),
        };

        var command = new FilterCommand
        {
            ExpressionText = "map(. == .)",
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => command.ExecuteAsync(shell, state, string.Empty, cts.Token));
    }
}