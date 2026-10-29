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

        var (state, interrupted) = await Program.ExecuteStartupCommandAsync(shell, command);

        var error = Assert.IsType<ErrorCommandState>(state);
        Assert.True(interrupted);
        Assert.NotEqual(ShellExitCode.Success, error.ExitCode);
        Assert.True(Assert.IsAssignableFrom<OperationCanceledException>(error.Exception.GetBaseException()).CancellationToken.IsCancellationRequested);
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
    [InlineData("startup-handled-test | echo downstream")]
    [InlineData("{ startup-handled-test; echo downstream }")]
    [InlineData("loop { startup-handled-test }")]
    [InlineData("while true { startup-handled-test }")]
    [InlineData("do { startup-handled-test } while true")]
    [InlineData("for $item in [1,2] { startup-handled-test }")]
    [InlineData("def stop { startup-handled-test; echo downstream }; stop")]
    [InlineData("script")]
    public async Task HandledCancellation_StopsEnclosingRunnersSuccessfully(string command)
    {
        using var shell = ShellInterpreter.CreateInstance();
        Assert.True(CommandFactory.TryCreateFactory(typeof(StartupHandledCancellationCommand), out var factory));
        shell.App.Commands["startup-handled-test"] = factory;
        var script = $"startup-cancel-{Guid.NewGuid():N}.csh";
        try
        {
            if (command == "script")
            {
                await File.WriteAllTextAsync(script, "startup-handled-test\necho downstream", TestContext.Current.CancellationToken);
                command = script;
            }

            var (state, interrupted) = await Program.ExecuteStartupCommandAsync(shell, command);

            Assert.False(state.IsError);
            Assert.True(interrupted);
            Assert.True(state.UserCancellationHandled);
            Assert.Equal("handled", Assert.IsType<ShellText>(state.Result).Text);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [CosmosCommand("startup-handled-test")]
    internal sealed class StartupHandledCancellationCommand : CosmosCommand
    {
        public override Task<CommandState> ExecuteAsync(
            ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
        {
            shell.CancelPrompt();
            commandState.UserCancellationHandled = true;
            commandState.Result = new ShellText("handled");
            return Task.FromResult(commandState);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task UnhandledCancellation_DoesNotRedirectPartialSuccess(bool script, bool pipeline)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "json", Quiet = true };
        Assert.True(CommandFactory.TryCreateFactory(typeof(StartupCancellationCommand), out var factory));
        shell.App.Commands["startup-cancel-test"] = factory;
        var output = Path.GetTempFileName();
        var file = $"startup-cancel-{Guid.NewGuid():N}.csh";
        const string sentinel = "previous complete result";
        try
        {
            await File.WriteAllTextAsync(output, sentinel, TestContext.Current.CancellationToken);
            var inner = "startup-cancel-test" + (pipeline ? " | echo downstream" : "");
            var command = $"{{ {inner} > {JsonSerializer.Serialize(output)} }}";
            if (script)
            {
                await File.WriteAllTextAsync(file, $"{inner} > {JsonSerializer.Serialize(output)}", TestContext.Current.CancellationToken);
                command = file;
            }

            var (state, interrupted) = await Program.ExecuteStartupCommandAsync(shell, command);

            Assert.True(state.IsError);
            Assert.True(interrupted);
            Assert.Equal(sentinel, await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
            Assert.False((await shell.ExecuteCommandAsync("echo next", TestContext.Current.CancellationToken)).IsError);
        }
        finally
        {
            File.Delete(output);
            File.Delete(file);
        }
    }

    [Fact]
    public async Task StartupCancellation_RendersStructuredPartialResults()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "json" };
        Assert.True(CommandFactory.TryCreateFactory(typeof(StartupPartialCancellationCommand), out var factory));
        shell.App.Commands["startup-partial-test"] = factory;
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var savedOutput = Console.Out;
        var savedError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(errors);

            var (state, interrupted) = await Program.ExecuteStartupCommandAsync(shell, "startup-partial-test");

            Assert.IsType<StructuredErrorCommandState>(state);
            Assert.True(interrupted);
            Assert.True(state.IsError);
            Assert.Empty(output.ToString());
            using var payload = JsonDocument.Parse(errors.ToString());
            Assert.Equal("error", payload.RootElement.GetProperty("status").GetString());
            var result = payload.RootElement.GetProperty("result");
            Assert.Equal(1, result.GetProperty("succeeded").GetInt32());
            Assert.Equal(1, result.GetProperty("uncertain").GetInt32());
            Assert.True(result.GetProperty("resultIncomplete").GetBoolean());
        }
        finally
        {
            Console.SetOut(savedOutput);
            Console.SetError(savedError);
        }
    }

    [CosmosCommand("startup-partial-test")]
    internal sealed class StartupPartialCancellationCommand : CosmosCommand
    {
        public override Task<CommandState> ExecuteAsync(
            ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
        {
            shell.CancelPrompt();
            return Task.FromResult<CommandState>(new StructuredErrorCommandState(
                new OperationCanceledException(token),
                new ShellJson(JsonSerializer.SerializeToElement(new { succeeded = 1, uncertain = 1, resultIncomplete = true }))));
        }
    }

    [Fact]
    public async Task PromptRefresh_DoesNotCancelQueuedStartupWork()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = shell.RunSerializedAsync(async () =>
        {
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            shell.RefreshPrompt();
            return 0;
        }, TestContext.Current.CancellationToken);
        try
        {
            var queued = Program.ExecuteStartupCommandAsync(shell, "echo queued");
            Assert.False(queued.IsCompleted);
            release.TrySetResult();
            await holder;

            var (state, interrupted) = await queued;

            Assert.False(interrupted);
            Assert.False(state.IsError);
            Assert.Equal("queued", Assert.IsType<ShellText>(state.Result).Text);
        }
        finally
        {
            release.TrySetResult();
            await holder;
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
