// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

namespace CosmosShell.Tests.CommandTests;

public class MakeItemCommandTests
{
    [Fact]
    public void ParseJsonDictionary()
    {
        var result = MakeItemCommand.ParseJson("{ \"foo\": \"Bar\", \"test\":42}");
        var dict = result as IDictionary<string, object>;
        Assert.NotNull(dict);
        Assert.Equal(2, dict.Count);
        Assert.Equal(typeof(string), dict["foo"].GetType());
        Assert.Equal("Bar", dict["foo"]);
        Assert.Equal(42, (int)dict["test"]);
    }

    [Fact]
    public void ParseJsonList()
    {
        var result = MakeItemCommand.ParseJson("[{ \"foo\": \"Bar\", \"test\":42}]");
        var list = result as IList<object>;
        Assert.NotNull(list);
        Assert.Single(list);
        var dict = list[0] as Dictionary<string, object>;
        Assert.NotNull(dict);
        Assert.Equal("Bar", dict["foo"]);
        Assert.Equal(42, (int)dict["test"]);
    }

    [Fact]
    public void ParseJson_NumericKinds_PromoteToNarrowestType()
    {
        var dict = Assert.IsAssignableFrom<IDictionary<string, object>>(
            MakeItemCommand.ParseJson(
                "{ \"i\": 7, \"l\": 5000000000, \"d\": 1.5 }"));

        Assert.Equal(7, Assert.IsType<int>(dict["i"]));
        Assert.Equal(5000000000L, Assert.IsType<long>(dict["l"]));
        Assert.Equal(1.5d, Assert.IsType<double>(dict["d"]));
    }

    [Fact]
    public void ParseJson_BooleansAndNull_MapToClrEquivalents()
    {
        var dict = Assert.IsAssignableFrom<IDictionary<string, object>>(
            MakeItemCommand.ParseJson(
                "{ \"t\": true, \"f\": false, \"n\": null }"));

        Assert.True(Assert.IsType<bool>(dict["t"]));
        Assert.False(Assert.IsType<bool>(dict["f"]));
        Assert.Null(dict["n"]);
    }

    [Fact]
    public void ParseJson_NestedArraysAndObjects_AreParsedRecursively()
    {
        var dict = Assert.IsAssignableFrom<IDictionary<string, object>>(
            MakeItemCommand.ParseJson(
                "{ \"tags\": [\"a\", \"b\"], \"meta\": { \"k\": 1 } }"));

        var tags = Assert.IsAssignableFrom<IList<object>>(dict["tags"]);
        Assert.Equal(["a", "b"], tags.Cast<string>().ToArray());

        var meta = Assert.IsAssignableFrom<IDictionary<string, object>>(dict["meta"]);
        Assert.Equal(1, Assert.IsType<int>(meta["k"]));
    }

    [Fact]
    public async Task ExecuteAsync_NoDataAndNoPipeInput_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new MakeItemCommand { Data = null };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "mkitem", TestContext.Current.CancellationToken));
        Assert.Equal(MessageService.GetString("error-no_input_data"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithData_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new MakeItemCommand { Data = "{\"id\":\"1\"}" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "mkitem '{\"id\":\"1\"}'", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_PipeInput_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new MakeItemCommand();
        var commandState = new CommandState { Result = new ShellText("{\"id\":\"2\"}") };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, commandState, "mkitem", TestContext.Current.CancellationToken));
    }
}

