// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Util;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using System.Reflection;
using System.Text.Json;

[Collection(ConsoleOutputTestCollection.Name)]
public class StartupOptionTests
{
    [Theory]
    [InlineData(ConsoleKey.C, true, true)]
    [InlineData(ConsoleKey.N, false, false)]
    [InlineData(ConsoleKey.Escape, false, false)]
    public async Task Confirmation_InterruptStopsFollowingCommandsButDeclineDoesNot(ConsoleKey key, bool control, bool interrupted)
    {
        using var shell = ShellInterpreter.CreateInstance();
        Assert.True(CommandFactory.TryCreateFactory(typeof(StartupConfirmationCommand), out var factory));
        shell.App.Commands["startup-confirm-test"] = factory;
        StartupConfirmationCommand.Key = new ConsoleKeyInfo(
            key == ConsoleKey.N ? 'n' : '\0', key, false, false, control);
        var output = Path.GetFullPath($"startup-following-{Guid.NewGuid():N}.txt");
        try
        {
            var (state, wasInterrupted) = await Program.ExecuteStartupCommandAsync(
                shell, $"{{ startup-confirm-test; echo downstream > {JsonSerializer.Serialize(output)} }}");

            Assert.Equal(interrupted, wasInterrupted);
            Assert.Equal(interrupted, state.IsError);
            Assert.Equal(!interrupted, File.Exists(output));
            Assert.False((await shell.ExecuteCommandAsync("echo next", TestContext.Current.CancellationToken)).IsError);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [CosmosCommand("startup-confirm-test")]
    internal sealed class StartupConfirmationCommand : CosmosCommand
    {
        internal static ConsoleKeyInfo Key { get; set; }

        public override Task<CommandState> ExecuteAsync(
            ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
        {
            ShellInterpreter.Confirm("yes_char", shell, () => Key);
            return Task.FromResult(commandState);
        }
    }

    [Theory]
    [InlineData("json", false, true, false, false)]
    [InlineData("csv", false, true, false, false)]
    [InlineData("user", false, false, false, false)]
    [InlineData("table", false, true, true, false)]
    [InlineData("user", true, true, false, false)]
    [InlineData("user", false, true, false, true)]
    public void ConsoleCancellation_KeepsMachineOutputClean(
        string format, bool quiet, bool terminal, bool redirected, bool markerExpected)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = format, Quiet = quiet };
        shell.IsOutputTerminal = () => terminal;
        shell.StdOutRedirect = redirected ? "not-written.txt" : null;
        using var cancellation = ShellInterpreter.UserCancellationTokenSource;
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var savedOutput = Console.Out;
        var savedError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(errors);
            RaiseConsoleCancellation(shell);

            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(markerExpected ? "^C" + Environment.NewLine : "", output.ToString());
            Assert.Empty(errors.ToString());
        }
        finally
        {
            Console.SetOut(savedOutput);
            Console.SetError(savedError);
        }
    }

    [Fact]
    public async Task RedirectedInput_CancellationAbortsBlockingReadWithoutExecutingLateScript()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Quiet = true };
        using var input = new BlockingStartupReader();
        var output = Path.GetFullPath($"startup-stdin-{Guid.NewGuid():N}.txt");
        input.Script = $"echo should-not-run > {JsonSerializer.Serialize(output)}";
        try
        {
            var startup = Program.ExecuteStartupInputAsync(shell, input);
            await input.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            RaiseConsoleCancellation(shell);
            var (state, interrupted) = await startup.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.True(interrupted);
            Assert.True(Assert.IsType<ErrorCommandState>(state).IsError);
            Assert.True(input.ReadToken.IsCancellationRequested);
            input.Release.Set();
            await input.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(File.Exists(output));
            Assert.False((await shell.ExecuteCommandAsync("echo next", TestContext.Current.CancellationToken)).IsError);
        }
        finally
        {
            input.Release.Set();
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n")]
    public async Task RedirectedInput_EmptyInputPreservesInteractiveStartup(string text)
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var input = new StringReader(text);

        var (state, interrupted) = await Program.ExecuteStartupInputAsync(shell, input);

        Assert.Null(state);
        Assert.False(interrupted);
    }

    [Fact]
    public async Task RedirectedInput_HandledCancellationRemainsSuccessfulAndStopsScript()
    {
        using var shell = ShellInterpreter.CreateInstance();
        Assert.True(CommandFactory.TryCreateFactory(typeof(StartupHandledCancellationCommand), out var factory));
        shell.App.Commands["startup-handled-test"] = factory;
        using var input = new StringReader("startup-handled-test\necho downstream");

        var (state, interrupted) = await Program.ExecuteStartupInputAsync(shell, input);

        Assert.True(interrupted);
        Assert.NotNull(state);
        Assert.False(state.IsError);
        Assert.True(state.UserCancellationHandled);
        Assert.Equal("handled", Assert.IsType<ShellText>(state.Result).Text);
    }

    private static void RaiseConsoleCancellation(ShellInterpreter shell)
    {
        var args = (ConsoleCancelEventArgs)Activator.CreateInstance(
            typeof(ConsoleCancelEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [ConsoleSpecialKey.ControlC], culture: null)!;
        typeof(ShellInterpreter).GetMethod("Console_CancelKeyPress", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(shell, [null, args]);
        Assert.True(args.Cancel);
    }

    private sealed class BlockingStartupReader : TextReader
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        internal string Script { get; set; } = "";
        internal CancellationToken ReadToken { get; private set; }

        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            this.ReadToken = cancellationToken;
            this.Started.SetResult();
            this.Release.Wait();
            this.Finished.SetResult();
            return Task.FromResult(this.Script);
        }

        protected override void Dispose(bool disposing)
        {
            this.Release.Set();
            base.Dispose(disposing);
        }
    }

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
