// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[Collection(ConsoleOutputTestCollection.Name)]
public class PwdCommandTests
{
    [Theory]
    [InlineData("offline", "not connected")]
    [InlineData("root", "/")]
    [InlineData("database", "/Db[Name]")]
    [InlineData("container", "/Db[Name]/Con[tainer]")]
    public async Task PwdCommand_RendersBothLocationsWithoutChangingNavigation(string scope, string expectedLocation)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = scope switch
        {
            "offline" => new DisconnectedState(),
            "root" => new ConnectedState(null!),
            "database" => new DatabaseState("Db[Name]", null!),
            "container" => new ContainerState("Con[tainer]", "Db[Name]", null!),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
        var navigationState = shell.State;
        var localDirectory = Directory.GetCurrentDirectory();
        using var output = new StringWriter();
        var originalConsole = AnsiConsole.Console;
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            });
            AnsiConsole.Console.Profile.Width = 2000;

            var state = await new PwdCommand().ExecuteAsync(
                shell, new CommandState(), "pwd", TestContext.Current.CancellationToken);

            Assert.Empty(output.ToString());
            Assert.Same(navigationState, shell.State);
            Assert.Equal(localDirectory, Directory.GetCurrentDirectory());
            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(localDirectory, result.GetProperty("localDirectory").GetString());
            Assert.Equal(scope is "database" or "container" ? "Db[Name]" : null, result.GetProperty("database").GetString());
            Assert.Equal(scope == "container" ? "Con[tainer]" : null, result.GetProperty("container").GetString());

            shell.State = new DisconnectedState();
            Assert.NotNull(state.RenderUser);
            state.RenderUser();

            Assert.Equal(
                $"Cosmos location: {expectedLocation}{Environment.NewLine}Local directory: {localDirectory}{Environment.NewLine}",
                output.ToString());
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
        }
    }

    [Fact]
    public async Task PwdCommand_WhenDisconnected_ReturnsNotConnected()
    {
        var shell = ShellInterpreter.CreateInstance();

        var state = await shell.ExecuteCommandAsync("pwd", CancellationToken.None);

        Assert.False(state.IsError);
        Assert.NotNull(state.RenderUser);
        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.Equal(JsonValueKind.Null, result.Value.GetProperty("currentLocation").ValueKind);
    }

    [Fact]
    public async Task PwdCommand_WhenConnectedAtRoot_ReturnsRootLocation()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(null!);

        var state = await shell.ExecuteCommandAsync("pwd", CancellationToken.None);

        Assert.False(state.IsError);
        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.Equal("/", result.Value.GetProperty("currentLocation").GetString());
    }

    [Fact]
    public async Task PwdCommand_InDatabaseState_ReturnsDatabaseLocation()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", null!);

        var state = await shell.ExecuteCommandAsync("pwd", CancellationToken.None);

        Assert.False(state.IsError);
        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.Equal("/TestDatabase", result.Value.GetProperty("currentLocation").GetString());
    }

    [Fact]
    public async Task PwdCommand_InContainerState_ReturnsContainerLocation()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", null!);

        var state = await shell.ExecuteCommandAsync("pwd", CancellationToken.None);

        Assert.False(state.IsError);
        var result = Assert.IsType<ShellJson>(state.Result);
        Assert.Equal("/TestDatabase/TestContainer", result.Value.GetProperty("currentLocation").GetString());
    }

    [Fact]
    public void GetCurrentLocationMarkup_EscapesMarkupCharacters()
    {
        var markup = ShellLocation.GetCurrentLocationMarkup(new ContainerState("Con[tainer]", "Db[Name]", null!));

        Assert.Contains(Markup.Escape("Db[Name]"), markup);
        Assert.Contains(Markup.Escape("Con[tainer]"), markup);
    }
}