// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Util;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using System.Text.Json;

[Collection(ConsoleOutputTestCollection.Name)]
public class StartupOptionTests
{
    [Theory]
    [InlineData("-c")]
    [InlineData("-k")]
    [InlineData("stdin")]
    public async Task StartupCommands_ObserveConsoleCancellation(string mode)
    {
        using var shell = ShellInterpreter.CreateInstance();
        Assert.True(CommandFactory.TryCreateFactory(typeof(StartupCancellationCommand), out var factory));
        shell.App.Commands["startup-cancel-test"] = factory;
        string command;
        if (mode == "stdin")
        {
            command = "startup-cancel-test\n";
        }
        else
        {
            var (root, map) = Program.BuildRootCommand();
            var parsed = root.Parse(Program.NormalizeArguments([mode, "startup-cancel-test"]));
            Assert.Empty(parsed.Errors);
            command = parsed.GetValue(mode == "-c" ? map.ExecuteAndQuit : map.ExecuteAndContinue)!;
        }

        var state = await Program.ExecuteStartupCommandAsync(shell, command);

        Assert.False(state.IsError);
        Assert.True(Assert.IsType<ShellJson>(state.Result).Value.GetBoolean());
    }

    [CosmosCommand("startup-cancel-test")]
    internal sealed class StartupCancellationCommand : CosmosCommand
    {
        public override Task<CommandState> ExecuteAsync(
            ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
        {
            shell.CancelPrompt();
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(token.IsCancellationRequested));
            return Task.FromResult(commandState);
        }
    }

    [Theory]
    [InlineData("--output", "JSON", "json")]
    [InlineData("-o", "csv", "csv")]
    [InlineData("--output", "table", "table")]
    public void OutputAliases_PreserveResultFormat(string option, string value, string expected)
    {
        var (command, map) = Program.BuildRootCommand();
        var result = command.Parse([option, value]);

        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.GetValue(map.Output));
    }

    [Fact]
    public void DefaultsAndOptionalValues_PreserveExistingBehavior()
    {
        var (command, map) = Program.BuildRootCommand();
        var empty = command.Parse([]);
        Assert.Empty(empty.Errors);
        Assert.Equal(2, empty.GetValue(map.ColorSystem));
        Assert.Null(empty.GetResult(map.McpPort));
        Assert.Null(empty.GetResult(map.Diagnostics));
        Assert.Null(empty.GetResult(map.Otel));

        var optional = command.Parse(["--mcp", "--diagnostics", "--otel"]);
        Assert.Empty(optional.Errors);
        Assert.NotNull(optional.GetResult(map.McpPort));
        Assert.Null(optional.GetValue(map.McpPort));
        Assert.NotNull(optional.GetResult(map.Diagnostics));
        Assert.NotNull(optional.GetResult(map.Otel));

        var explicitPort = command.Parse(["--mcp", "6129"]);
        Assert.Empty(explicitPort.Errors);
        Assert.Equal(6129, explicitPort.GetValue(map.McpPort));
    }

    [Theory]
    [InlineData("-c", "--help")]
    [InlineData("-c", "-h")]
    [InlineData("-c", "-?")]
    [InlineData("-c", "/?")]
    [InlineData("-c", "/h")]
    [InlineData("-c", "--version")]
    [InlineData("-k", "--help")]
    [InlineData("-k", "-h")]
    [InlineData("-k", "-?")]
    [InlineData("-k", "/?")]
    [InlineData("-k", "/h")]
    [InlineData("-k", "--version")]
    [InlineData("/c", "--help")]
    [InlineData("/c", "--version")]
    [InlineData("/k", "--help")]
    [InlineData("/k", "--version")]
    public void LiteralCommandTail_IsNotParsedAsStartupHelpOrVersion(string option, string tail)
    {
        var (command, map) = Program.BuildRootCommand();
        Assert.DoesNotContain(command.Options, option => option is System.CommandLine.Help.HelpOption or System.CommandLine.VersionOption);
        var normalized = Program.NormalizeArguments([option, tail]);
        var result = command.Parse(normalized);

        Assert.Empty(Program.TakePreCommandArgs(normalized));
        Assert.Empty(result.Errors);
        Assert.Equal(tail, result.GetValue(normalized[0] == "-c" ? map.ExecuteAndQuit : map.ExecuteAndContinue));
    }

    [Theory]
    [InlineData("--unknown", "help-error-UnknownOptionError", "option", "--unknown")]
    [InlineData("unexpected", "help-error-UnknownArgumentError", "argument", "unexpected")]
    [InlineData("--connect", "help-error-MissingValueOptionError", "option", "--connect")]
    [InlineData("--database", "help-error-MissingValueOptionError", "option", "--database")]
    [InlineData("--color-system", "help-error-MissingValueOptionError", "option", "--color-system")]
    [InlineData("-c", "help-error-MissingValueOptionError", "option", "-c")]
    [InlineData("-k", "help-error-MissingValueOptionError", "option", "-k")]
    [InlineData("--mcp=invalid", "help-error-BadFormatConversionError2", "option", "--mcp")]
    [InlineData("--color-system=invalid", "help-error-BadFormatConversionError2", "option", "--color-system")]
    [InlineData("--connect-mode=invalid", "help-error-BadFormatConversionError2", "option", "--connect-mode")]
    public void ParseErrors_RetainLocalizedMessages(string argument, string key, string parameter, string value)
    {
        var (command, _) = Program.BuildRootCommand();
        var result = command.Parse([argument]);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(MessageService.GetArgsString(key, parameter, value), Program.GetParseErrorMessages(result));
    }
}
