// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Verifies machine-mode warnings written to the process stderr. These tests replace
/// <see cref="Console.Error"/>, so they run in the non-parallel console collection.
/// </summary>
[Collection(ConsoleOutputTestCollection.Name)]
public class MachineModeConsoleWarningTests
{
    [CosmosCommand("warning-mutation-test")]
    internal sealed class WarningMutationTestCommand : CosmosCommand
    {
        internal static bool WarningRenderedBeforeMutation { get; set; }

        internal static bool ShouldFail { get; set; }

        internal static bool ShouldUpdateWarning { get; set; }

        public override Task<CommandState> ExecuteAsync(
            ShellInterpreter shell,
            CommandState commandState,
            string commandText,
            CancellationToken token)
        {
            var warning = commandState.AddWarning("mutation warning");
            shell.RenderMutationWarnings(commandState);
            WarningRenderedBeforeMutation = warning.IsRendered;

            if (ShouldFail)
            {
                throw new CommandException("warning-mutation-test", "mutation failed");
            }

            if (ShouldUpdateWarning)
            {
                warning.Message = "mutation warning for 2 items";
                shell.RenderUpdatedWarnings(commandState);
            }

            return Task.FromResult(commandState);
        }
    }

    [Fact]
    public async Task ScriptFailure_KeepsConsoleStderrAsSingleJsonPayload()
    {
        var (stderr, result) = await RunScriptAsync("warning-output-test\nwarning-failure-test");

        Assert.True(result.IsError);
        using var document = JsonDocument.Parse(stderr);
        Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
        Assert.Contains("script failure", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            "script warning",
            Assert.Single(document.RootElement.GetProperty("warnings").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task ScriptSuccess_ReportsDeferredWarningsOnConsoleStderr()
    {
        var (stderr, result) = await RunScriptAsync("warning-output-test\nwarning-output-test");

        Assert.False(result.IsError, (result as ErrorCommandState)?.Exception.ToString());
        Assert.Equal($"script warning{Environment.NewLine}script warning{Environment.NewLine}", stderr);
    }

    [Fact]
    public async Task MutationWarning_IsEmittedAsJsonLineBeforeTheMutation()
    {
        WarningMutationTestCommand.WarningRenderedBeforeMutation = false;
        WarningMutationTestCommand.ShouldFail = false;
        WarningMutationTestCommand.ShouldUpdateWarning = false;

        var (stderr, result) = await RunCommandAsync("warning-mutation-test");

        Assert.False(result.IsError, (result as ErrorCommandState)?.Exception.ToString());
        Assert.True(WarningMutationTestCommand.WarningRenderedBeforeMutation);
        using var warning = JsonDocument.Parse(Assert.Single(stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("warning", warning.RootElement.GetProperty("status").GetString());
        Assert.Equal("mutation warning", warning.RootElement.GetProperty("warning").GetString());
    }

    [Fact]
    public async Task MutationWarningAndFailure_AreSeparateJsonLines()
    {
        WarningMutationTestCommand.WarningRenderedBeforeMutation = false;
        WarningMutationTestCommand.ShouldFail = true;
        WarningMutationTestCommand.ShouldUpdateWarning = false;

        var (stderr, result) = await RunCommandAsync("warning-mutation-test");

        Assert.True(result.IsError);
        Assert.True(WarningMutationTestCommand.WarningRenderedBeforeMutation);
        var lines = stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var warning = JsonDocument.Parse(lines[0]);
        Assert.Equal("warning", warning.RootElement.GetProperty("status").GetString());
        Assert.Equal("mutation warning", warning.RootElement.GetProperty("warning").GetString());
        using var error = JsonDocument.Parse(lines[1]);
        Assert.Equal("error", error.RootElement.GetProperty("status").GetString());
        Assert.Equal("mutation failed", error.RootElement.GetProperty("error").GetString());
        Assert.False(error.RootElement.TryGetProperty("warnings", out _));
    }

    [Fact]
    public async Task UpdatedMutationWarning_IsEmittedAsAnotherJsonLine()
    {
        WarningMutationTestCommand.WarningRenderedBeforeMutation = false;
        WarningMutationTestCommand.ShouldFail = false;
        WarningMutationTestCommand.ShouldUpdateWarning = true;

        var (stderr, result) = await RunCommandAsync("warning-mutation-test");

        Assert.False(result.IsError, (result as ErrorCommandState)?.Exception.ToString());
        var lines = stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var initialWarning = JsonDocument.Parse(lines[0]);
        Assert.Equal("warning", initialWarning.RootElement.GetProperty("status").GetString());
        Assert.Equal("mutation warning", initialWarning.RootElement.GetProperty("warning").GetString());
        using var updatedWarning = JsonDocument.Parse(lines[1]);
        Assert.Equal("warning-update", updatedWarning.RootElement.GetProperty("status").GetString());
        Assert.Equal("mutation warning for 2 items", updatedWarning.RootElement.GetProperty("warning").GetString());
    }

    [Fact]
    public async Task ConsoleWarning_IsNotCapturedByLaterStatementRedirect()
    {
        var file = Path.GetTempFileName();
        try
        {
            var (stderr, result) = await RunCommandAsync(
                $"warning-render-test\nwarning-render-test 2> {ShellLiteral.Quote(file.Replace('\\', '/'))}");

            Assert.False(result.IsError, (result as ErrorCommandState)?.Exception.ToString());
            Assert.Equal($"rendered warning{Environment.NewLine}", stderr);
            Assert.Equal($"rendered warning{Environment.NewLine}", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ConsoleWarning_StaysOnConsoleWhenErrorEnvelopeIsRedirected()
    {
        var file = Path.GetTempFileName();
        try
        {
            var (stderr, result) = await RunCommandAsync(
                $"warning-render-test\nwarning-failure-test 2> {ShellLiteral.Quote(file.Replace('\\', '/'))}");

            Assert.True(result.IsError);
            Assert.Equal($"rendered warning{Environment.NewLine}", stderr);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
            Assert.False(document.RootElement.TryGetProperty("warnings", out _));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task OutputRedirectFailure_KeepsConsoleWarningInErrorEnvelope()
    {
        var missingDirectory = Path.Join(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        var output = Path.Join(missingDirectory, "out.json").Replace('\\', '/');

        var (stderr, result) = await RunCommandAsync($"warning-output-test > {ShellLiteral.Quote(output)}");

        Assert.True(result.IsError);
        using var document = JsonDocument.Parse(stderr);
        Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "script warning",
            Assert.Single(document.RootElement.GetProperty("warnings").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task VerboseOutputRedirectFailure_StillReportsConsoleWarning()
    {
        var missingDirectory = Path.Join(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        var output = Path.Join(missingDirectory, "out.json").Replace('\\', '/');

        var (stderr, result) = await RunCommandAsync($"warning-output-test > {ShellLiteral.Quote(output)}", verbose: true);

        Assert.True(result.IsError);
        Assert.Equal("script warning", Assert.Single(result.Warnings).Message);
        Assert.Contains("script warning", stderr);
    }

    [Fact]
    public async Task RedirectedWarning_DoesNotFlushEarlierConsoleWarningBeforeFailure()
    {
        var file = Path.GetTempFileName();
        try
        {
            var (stderr, result) = await RunScriptAsync(
                "warning-render-test\n"
                    + $"warning-render-test 2> {ShellLiteral.Quote(file.Replace('\\', '/'))}\n"
                    + "warning-failure-test");

            Assert.True(result.IsError);
            using var document = JsonDocument.Parse(stderr);
            Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "rendered warning",
                Assert.Single(document.RootElement.GetProperty("warnings").EnumerateArray()).GetString());
            Assert.Equal($"rendered warning{Environment.NewLine}", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static async Task<(string Stderr, CommandState Result)> RunScriptAsync(string scriptContent)
    {
        var script = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(script, scriptContent, TestContext.Current.CancellationToken);
            return await RunCommandAsync($"exec {ShellLiteral.Quote(script.Replace('\\', '/'))}");
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static async Task<(string Stderr, CommandState Result)> RunCommandAsync(string command, bool verbose = false)
    {
        using var shell = ShellInterpreter.CreateInstance();
        Assert.True(CommandFactory.TryCreateFactory(typeof(CommandStatementTests.WarningOutputTestCommand), out var warningFactory));
        Assert.True(CommandFactory.TryCreateFactory(typeof(CommandStatementTests.WarningRenderTestCommand), out var renderFactory));
        Assert.True(CommandFactory.TryCreateFactory(typeof(CommandStatementTests.WarningFailureTestCommand), out var failureFactory));
        Assert.True(CommandFactory.TryCreateFactory(typeof(WarningMutationTestCommand), out var mutationFactory));
        shell.App.Commands["warning-output-test"] = warningFactory;
        shell.App.Commands["warning-render-test"] = renderFactory;
        shell.App.Commands["warning-failure-test"] = failureFactory;
        shell.App.Commands["warning-mutation-test"] = mutationFactory;
        shell.Options = new Program.CosmosShellOptions { Output = "json", Verbose = verbose };
        var originalError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            var result = await shell.ExecuteCommandAsync(command, TestContext.Current.CancellationToken);
            return (error.ToString(), result);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
