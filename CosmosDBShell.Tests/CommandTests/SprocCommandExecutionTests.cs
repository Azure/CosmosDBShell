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
/// Unit tests for the SDK-calling subcommand methods on <see cref="SprocCommand"/>.
/// The Cosmos emulator does not support stored procedures, so the <see cref="Scripts"/>
/// surface is mocked to drive list/show/exists/create/exec/delete behavior offline.
/// </summary>
public class SprocCommandExecutionTests
{
    private static StoredProcedureResponse StoredProcedureResponse(string id, string? body, double charge = 1.0)
    {
        var response = Substitute.For<StoredProcedureResponse>();
        response.Resource.Returns(new StoredProcedureProperties { Id = id, Body = body });
        response.RequestCharge.Returns(charge);
        return response;
    }

    [Fact]
    public async Task ListAsync_NoProcedures_ReturnsEmptyJsonArray()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var iterator = ScriptMocks.SinglePage(Array.Empty<StoredProcedureProperties>());
        scripts.GetStoredProcedureQueryIterator<StoredProcedureProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var command = new SprocCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, ShellInterpreter.CreateInstance(), new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(0, json.GetArrayLength());
        Assert.True(state.IsPrinted);
    }

    [Fact]
    public async Task ListAsync_WithProcedures_ProjectsMetadata()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var items = new[]
        {
            new StoredProcedureProperties { Id = "alpha", Body = "function alpha() {}" },
            new StoredProcedureProperties { Id = "beta", Body = "function beta() {}" },
        };
        var iterator = ScriptMocks.SinglePage(items);
        scripts.GetStoredProcedureQueryIterator<StoredProcedureProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var command = new SprocCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, ShellInterpreter.CreateInstance(), new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(2, json.GetArrayLength());
        Assert.Equal("alpha", json[0].GetProperty("id").GetString());
        Assert.Equal("function alpha() {}".Length, json[0].GetProperty("bodyLength").GetInt32());
        Assert.True(state.IsPrinted);
    }

    [Fact]
    public async Task ListAsync_WhenRedirected_ReturnsJsonResultWithoutPrinting()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var iterator = ScriptMocks.SinglePage(new[] { new StoredProcedureProperties { Id = "alpha", Body = "x" } });
        scripts.GetStoredProcedureQueryIterator<StoredProcedureProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var shell = ShellInterpreter.CreateInstance();
        shell.StdOutRedirect = "out.json";

        var command = new SprocCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, shell, new CommandState(), CancellationToken.None);

        Assert.False(state.IsPrinted);
        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task ShowAsync_ReturnsBodyText()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = StoredProcedureResponse("alpha", "function alpha() {}");
        scripts.ReadStoredProcedureAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new SprocCommand { Subcommand = "show", Name = "alpha" };
        var state = await command.ShowAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal("function alpha() {}", state.Result!.ConvertShellObject(DataType.Text));
    }

    [Fact]
    public async Task ShowAsync_NotFound_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.ReadStoredProcedureAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredProcedureResponse>(ScriptMocks.NotFound()));

        var command = new SprocCommand { Subcommand = "show", Name = "missing" };

        await Assert.ThrowsAsync<CommandException>(() => command.ShowAsync(container, new CommandState(), CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_WhenPresent_ReturnsTrue()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = StoredProcedureResponse("alpha", "x");
        scripts.ReadStoredProcedureAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new SprocCommand { Subcommand = "exists", Name = "alpha" };
        var state = await command.ExistsAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal(true, state.Result!.ConvertShellObject(DataType.Boolean));
        Assert.True(state.IsPrinted);
    }

    [Fact]
    public async Task ExistsAsync_WhenMissing_ReturnsFalse()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.ReadStoredProcedureAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredProcedureResponse>(ScriptMocks.NotFound()));

        var command = new SprocCommand { Subcommand = "exists", Name = "missing" };
        var state = await command.ExistsAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal(false, state.Result!.ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public async Task WriteCreateAsync_Create_ReturnsId()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = StoredProcedureResponse("alpha", "x");
        scripts.CreateStoredProcedureAsync(Arg.Any<StoredProcedureProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new SprocCommand { Subcommand = "create", Name = "alpha" };
        var state = await command.WriteCreateAsync(container, new CommandState(), "alpha", "function alpha() {}", force: false, CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal("alpha", json.GetProperty("id").GetString());
    }

    [Fact]
    public async Task WriteCreateAsync_Force_ReplacesExisting()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = StoredProcedureResponse("alpha", "x");
        scripts.ReplaceStoredProcedureAsync(Arg.Any<StoredProcedureProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new SprocCommand { Subcommand = "create", Name = "alpha", Force = true };
        var state = await command.WriteCreateAsync(container, new CommandState(), "alpha", "function alpha() {}", force: true, CancellationToken.None);

        await scripts.Received(1).ReplaceStoredProcedureAsync(Arg.Any<StoredProcedureProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>());
        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal("alpha", json.GetProperty("id").GetString());
    }

    [Fact]
    public async Task WriteCreateAsync_Conflict_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.CreateStoredProcedureAsync(Arg.Any<StoredProcedureProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredProcedureResponse>(ScriptMocks.Conflict()));

        var command = new SprocCommand { Subcommand = "create", Name = "alpha" };

        await Assert.ThrowsAsync<CommandException>(() =>
            command.WriteCreateAsync(container, new CommandState(), "alpha", "function alpha() {}", force: false, CancellationToken.None));
    }

    [Fact]
    public async Task ExecAsync_ReturnsResource()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        using var doc = JsonDocument.Parse("{\"ok\":true}");
        var execResponse = Substitute.For<StoredProcedureExecuteResponse<JsonElement>>();
        execResponse.Resource.Returns(doc.RootElement.Clone());
        execResponse.RequestCharge.Returns(2.0);
        scripts.ExecuteStoredProcedureAsync<JsonElement>(
                Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<dynamic[]>(), Arg.Any<StoredProcedureRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(execResponse));

        var command = new SprocCommand { Subcommand = "exec", Name = "alpha", PartitionKey = "pk1" };
        var state = await command.ExecAsync(container, new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.True(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ExecAsync_MissingPartitionKey_ThrowsCommandException()
    {
        var (container, _) = ScriptMocks.NewContainer();
        var command = new SprocCommand { Subcommand = "exec", Name = "alpha" };

        await Assert.ThrowsAsync<CommandException>(() => command.ExecAsync(container, new CommandState(), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RemovesProcedure()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = StoredProcedureResponse("alpha", "x");
        scripts.DeleteStoredProcedureAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new SprocCommand { Subcommand = "delete", Name = "alpha" };
        var state = await command.DeleteAsync(container, new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.True(json.GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.DeleteStoredProcedureAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredProcedureResponse>(ScriptMocks.NotFound()));

        var command = new SprocCommand { Subcommand = "delete", Name = "missing" };

        await Assert.ThrowsAsync<CommandException>(() => command.DeleteAsync(container, new CommandState(), CancellationToken.None));
    }
}
