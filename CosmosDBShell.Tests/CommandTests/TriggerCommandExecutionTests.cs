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
/// Unit tests for the SDK-calling subcommand methods on <see cref="TriggerCommand"/>.
/// The Cosmos emulator does not support triggers, so the <see cref="Scripts"/>
/// surface is mocked to drive list/show/exists/create/delete behavior offline.
/// </summary>
public class TriggerCommandExecutionTests
{
    private static TriggerResponse TriggerResponse(string id, string? body, double charge = 1.0)
    {
        var response = Substitute.For<TriggerResponse>();
        response.Resource.Returns(new TriggerProperties
        {
            Id = id,
            Body = body,
            TriggerType = TriggerType.Pre,
            TriggerOperation = TriggerOperation.Create,
        });
        response.RequestCharge.Returns(charge);
        return response;
    }

    [Fact]
    public async Task ListAsync_NoTriggers_ReturnsEmptyJsonArray()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var iterator = ScriptMocks.SinglePage(Array.Empty<TriggerProperties>());
        scripts.GetTriggerQueryIterator<TriggerProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var command = new TriggerCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, ShellInterpreter.CreateInstance(), new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(0, json.GetArrayLength());
        Assert.True(state.IsPrinted);
    }

    [Fact]
    public async Task ListAsync_WithTriggers_ProjectsMetadata()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var items = new[]
        {
            new TriggerProperties
            {
                Id = "audit",
                Body = "function audit() {}",
                TriggerType = TriggerType.Pre,
                TriggerOperation = TriggerOperation.Create,
            },
        };
        var iterator = ScriptMocks.SinglePage(items);
        scripts.GetTriggerQueryIterator<TriggerProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var command = new TriggerCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, ShellInterpreter.CreateInstance(), new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(1, json.GetArrayLength());
        Assert.Equal("audit", json[0].GetProperty("id").GetString());
        Assert.Equal("Pre", json[0].GetProperty("triggerType").GetString());
        Assert.Equal("Create", json[0].GetProperty("triggerOperation").GetString());
    }

    [Fact]
    public async Task ListAsync_WhenRedirected_ReturnsJsonResultWithoutPrinting()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var iterator = ScriptMocks.SinglePage(new[]
        {
            new TriggerProperties
            {
                Id = "audit",
                Body = "x",
                TriggerType = TriggerType.Post,
                TriggerOperation = TriggerOperation.Delete,
            },
        });
        scripts.GetTriggerQueryIterator<TriggerProperties>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var shell = ShellInterpreter.CreateInstance();
        shell.StdOutRedirect = "out.json";

        var command = new TriggerCommand { Subcommand = "list" };
        var state = await command.ListAsync(container, shell, new CommandState(), CancellationToken.None);

        Assert.False(state.IsPrinted);
        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task ShowAsync_ReturnsBodyText()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = TriggerResponse("audit", "function audit() {}");
        scripts.ReadTriggerAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new TriggerCommand { Subcommand = "show", Name = "audit" };
        var state = await command.ShowAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal("function audit() {}", state.Result!.ConvertShellObject(DataType.Text));
    }

    [Fact]
    public async Task ShowAsync_NotFound_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.ReadTriggerAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TriggerResponse>(ScriptMocks.NotFound()));

        var command = new TriggerCommand { Subcommand = "show", Name = "missing" };

        await Assert.ThrowsAsync<CommandException>(() => command.ShowAsync(container, new CommandState(), CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_WhenPresent_ReturnsTrue()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = TriggerResponse("audit", "x");
        scripts.ReadTriggerAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new TriggerCommand { Subcommand = "exists", Name = "audit" };
        var state = await command.ExistsAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal(true, state.Result!.ConvertShellObject(DataType.Boolean));
        Assert.True(state.IsPrinted);
    }

    [Fact]
    public async Task ExistsAsync_WhenMissing_ReturnsFalse()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.ReadTriggerAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TriggerResponse>(ScriptMocks.NotFound()));

        var command = new TriggerCommand { Subcommand = "exists", Name = "missing" };
        var state = await command.ExistsAsync(container, new CommandState(), CancellationToken.None);

        Assert.Equal(false, state.Result!.ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public async Task WriteCreateAsync_Create_ReturnsMetadata()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = TriggerResponse("audit", "x");
        scripts.CreateTriggerAsync(Arg.Any<TriggerProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new TriggerCommand { Subcommand = "create", Name = "audit" };
        var state = await command.WriteCreateAsync(
            container, new CommandState(), "audit", "function audit() {}", TriggerType.Pre, TriggerOperation.Create, force: false, CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal("audit", json.GetProperty("id").GetString());
        Assert.Equal("Pre", json.GetProperty("triggerType").GetString());
        Assert.Equal("Create", json.GetProperty("triggerOperation").GetString());
    }

    [Fact]
    public async Task WriteCreateAsync_Force_ReplacesExisting()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = TriggerResponse("audit", "x");
        scripts.ReplaceTriggerAsync(Arg.Any<TriggerProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new TriggerCommand { Subcommand = "create", Name = "audit", Force = true };
        var state = await command.WriteCreateAsync(
            container, new CommandState(), "audit", "function audit() {}", TriggerType.Post, TriggerOperation.Update, force: true, CancellationToken.None);

        await scripts.Received(1).ReplaceTriggerAsync(Arg.Any<TriggerProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>());
        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.Equal("Post", json.GetProperty("triggerType").GetString());
        Assert.Equal("Update", json.GetProperty("triggerOperation").GetString());
    }

    [Fact]
    public async Task WriteCreateAsync_Conflict_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.CreateTriggerAsync(Arg.Any<TriggerProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TriggerResponse>(ScriptMocks.Conflict()));

        var command = new TriggerCommand { Subcommand = "create", Name = "audit" };

        await Assert.ThrowsAsync<CommandException>(() =>
            command.WriteCreateAsync(
                container, new CommandState(), "audit", "function audit() {}", TriggerType.Pre, TriggerOperation.Create, force: false, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTrigger()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        var response = TriggerResponse("audit", "x");
        scripts.DeleteTriggerAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var command = new TriggerCommand { Subcommand = "delete", Name = "audit" };
        var state = await command.DeleteAsync(container, new CommandState(), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(state.Result!.ConvertShellObject(DataType.Json));
        Assert.True(json.GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ThrowsCommandException()
    {
        var (container, scripts) = ScriptMocks.NewContainer();
        scripts.DeleteTriggerAsync(Arg.Any<string>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TriggerResponse>(ScriptMocks.NotFound()));

        var command = new TriggerCommand { Subcommand = "delete", Name = "missing" };

        await Assert.ThrowsAsync<CommandException>(() => command.DeleteAsync(container, new CommandState(), CancellationToken.None));
    }
}
