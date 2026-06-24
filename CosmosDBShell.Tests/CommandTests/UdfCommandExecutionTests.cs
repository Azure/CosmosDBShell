//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using NSubstitute;
using DataType = Azure.Data.Cosmos.Shell.Parser.DataType;

/// <summary>
/// Unit tests for the SDK-calling subcommand methods on <see cref="UdfCommand"/>.
/// The Cosmos emulator does not support user-defined functions, so the
/// <see cref="Scripts"/> surface is mocked to drive list/show/exists/create/delete.
/// </summary>
public class UdfCommandExecutionTests
{
    private static UserDefinedFunctionResponse UdfResponse(string id, string? body, double charge = 1.0)
    {
        var response = Substitute.For<UserDefinedFunctionResponse>();
        response.Resource.Returns(new UserDefinedFunctionProperties { Id = id, Body = body });
        response.RequestCharge.Returns(charge);
        return response;
    }

    [Fact]
    public async Task ListAsync_NoFunctions_ReturnsEmptyJsonArray()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var iterator = ScriptMocks.SinglePage(Array.Empty<UserDefinedFunctionProperties>());
        scripts.GetUserDefinedFunctionQueryIterator<UserDefinedFunctionProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var command = new UdfCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, ShellInterpreter.CreateInstance(), new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(0, json.GetArrayLength());
        Assert.True(state.IsPrinted);
    }

    [Fact]
    public async Task ListAsync_WithFunctions_ProjectsMetadata()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var items = new[]
        {
            new UserDefinedFunctionProperties { Id = "tax", Body = "function tax() {}" },
        };
        var iterator = ScriptMocks.SinglePage(items);
        scripts.GetUserDefinedFunctionQueryIterator<UserDefinedFunctionProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var command = new UdfCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, ShellInterpreter.CreateInstance(), new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(1, json.GetArrayLength());
        Assert.Equal("tax", json[0].GetProperty("id").GetString());
        Assert.Equal("function tax() {}".Length, json[0].GetProperty("bodyLength").GetInt32());
    }

    [Fact]
    public async Task ListAsync_WhenRedirected_ReturnsJsonResultWithoutPrinting()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var iterator = ScriptMocks.SinglePage(new[] { new UserDefinedFunctionProperties { Id = "tax", Body = "x" } });
        scripts.GetUserDefinedFunctionQueryIterator<UserDefinedFunctionProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var shell = ShellInterpreter.CreateInstance();
        shell.StdOutRedirect = "out.json";

        var command = new UdfCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, shell, new CommandState(), CancellationToken.None);

        Assert.False(state.IsPrinted);
        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task ShowAsync_ReturnsBodyText()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = UdfResponse("tax", "function tax() {}");
        scripts.ReadUserDefinedFunctionAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new UdfCommand { Subcommand = "show", Name = "tax" };
        var state = await command.ShowAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal("function tax() {}", state.Result!.ConvertShellObject(DataType.Text));
    }

    [Fact]
    public async Task ShowAsync_NotFound_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.ReadUserDefinedFunctionAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UserDefinedFunctionResponse>(ScriptMocks.NotFound()));

        var command = new UdfCommand { Subcommand = "show", Name = "missing" };

        await Assert.ThrowsAsync<CommandException>(() => command.ShowAsync(container, new CommandState(), CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_WhenPresent_ReturnsTrue()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = UdfResponse("tax", "x");
        scripts.ReadUserDefinedFunctionAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new UdfCommand { Subcommand = "exists", Name = "tax" };
        var state = await command.ExistsAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal(true, state.Result!.ConvertShellObject(DataType.Boolean));
        Assert.True(state.IsPrinted);
    }

    [Fact]
    public async Task ExistsAsync_WhenMissing_ReturnsFalse()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.ReadUserDefinedFunctionAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UserDefinedFunctionResponse>(ScriptMocks.NotFound()));

        var command = new UdfCommand { Subcommand = "exists", Name = "missing" };
        var state = await command.ExistsAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal(false, state.Result!.ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public async Task WriteCreateAsync_Create_ReturnsId()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = UdfResponse("tax", "x");
        scripts.CreateUserDefinedFunctionAsync(Arg.Any<UserDefinedFunctionProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new UdfCommand { Subcommand = "create", Name = "tax" };
        var state = await command.WriteCreateAsync(container, new CommandState(), "tax", "function tax() {}", force: false, CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal("tax", json.GetProperty("id").GetString());
    }

    [Fact]
    public async Task WriteCreateAsync_Force_ReplacesExisting()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = UdfResponse("tax", "x");
        scripts.ReplaceUserDefinedFunctionAsync(Arg.Any<UserDefinedFunctionProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new UdfCommand { Subcommand = "create", Name = "tax", Force = true };
        var state = await command.WriteCreateAsync(container, new CommandState(), "tax", "function tax() {}", force: true, CancellationToken.None);

        await scripts.Received(1).ReplaceUserDefinedFunctionAsync(Arg.Any<UserDefinedFunctionProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>());
        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal("tax", json.GetProperty("id").GetString());
    }

    [Fact]
    public async Task WriteCreateAsync_Conflict_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.CreateUserDefinedFunctionAsync(Arg.Any<UserDefinedFunctionProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UserDefinedFunctionResponse>(ScriptMocks.Conflict()));

        var command = new UdfCommand { Subcommand = "create", Name = "tax" };

        await Assert.ThrowsAsync<CommandException>(() =>
            command.WriteCreateAsync(container, new CommandState(), "tax", "function tax() {}", force: false, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFunction()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = UdfResponse("tax", "x");
        scripts.DeleteUserDefinedFunctionAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new UdfCommand { Subcommand = "delete", Name = "tax" };
        var state = await command.DeleteAsync(container, new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.True(json.GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.DeleteUserDefinedFunctionAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UserDefinedFunctionResponse>(ScriptMocks.NotFound()));

        var command = new UdfCommand { Subcommand = "delete", Name = "missing" };

        await Assert.ThrowsAsync<CommandException>(() => command.DeleteAsync(container, new CommandState(), CancellationToken.None));
    }
}
