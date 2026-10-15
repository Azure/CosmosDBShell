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

    private static async Task<(string Stderr, CommandState Result)> RunScriptAsync(string scriptContent)
    {
        using var shell = ShellInterpreter.CreateInstance();
        Assert.True(CommandFactory.TryCreateFactory(typeof(CommandStatementTests.WarningOutputTestCommand), out var warningFactory));
        Assert.True(CommandFactory.TryCreateFactory(typeof(CommandStatementTests.WarningFailureTestCommand), out var failureFactory));
        shell.App.Commands["warning-output-test"] = warningFactory;
        shell.App.Commands["warning-failure-test"] = failureFactory;
        shell.Options = new Program.CosmosShellOptions { Output = "json" };
        var script = Path.GetTempFileName();
        var originalError = Console.Error;
        using var error = new StringWriter();
        try
        {
            await File.WriteAllTextAsync(script, scriptContent, TestContext.Current.CancellationToken);
            Console.SetError(error);
            var result = await shell.ExecuteCommandAsync(
                $"exec {ShellLiteral.Quote(script.Replace('\\', '/'))}",
                TestContext.Current.CancellationToken);
            return (error.ToString(), result);
        }
        finally
        {
            Console.SetError(originalError);
            File.Delete(script);
        }
    }
}
