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
}