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
    [Theory]
    [InlineData("type")]
    [InlineData("true")]
    [InlineData(".items[] | .id")]
    public async Task ExecuteAsync_NormalizesExpressionResultToShellJson(string expression)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { items = new[] { new { id = "1" }, new { id = "2" } } })),
        };
        var command = new FilterCommand { ExpressionText = expression };

        var result = await command.ExecuteAsync(shell, state, string.Empty, TestContext.Current.CancellationToken);

        Assert.IsType<ShellJson>(result.Result);
    }

    [Theory]
    [InlineData(".[2147483648]", "2147483648")]
    [InlineData(".[2147483648]?", "2147483648")]
    [InlineData(".[999999999999999999999999999999]", "999999999999999999999999999999")]
    public async Task ExecuteAsync_OverflowingIndex_RejectsInputWithoutChangingResult(string expression, string index)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var input = new ShellJson(JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }));
        var state = new CommandState { Result = input };
        var command = new FilterCommand { ExpressionText = expression };

        var error = await Assert.ThrowsAsync<CommandException>(() =>
            command.ExecuteAsync(shell, state, string.Empty, TestContext.Current.CancellationToken));

        Assert.Contains(index, error.Message);
        Assert.Same(input, state.Result);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesPathExpression_AndPreservesStructuredResult()
    {
        using var shell = ShellInterpreter.CreateInstance();
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
        Assert.Null(result.RenderUser);
        Assert.IsType<ShellJson>(result.Result);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsCancellation()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonDocument.Parse("[1, 2, 3, 4, 5]").RootElement.Clone()),
        };

        var command = new FilterCommand
        {
            ExpressionText = "map(. == .)",
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => command.ExecuteAsync(shell, state, string.Empty, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_WrapsRuntimeErrorsInCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState
        {
            Result = new ShellJson(JsonDocument.Parse("{}").RootElement.Clone()),
        };

        var command = new FilterCommand
        {
            ExpressionText = "1 / 0",
        };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None));
    }
}
