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
        shell.App.Commands["warning-output-test"] = warningFactory;
        shell.App.Commands["warning-render-test"] = renderFactory;
        shell.App.Commands["warning-failure-test"] = failureFactory;
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
