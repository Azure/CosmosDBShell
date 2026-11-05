// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

using Xunit.Sdk;

/// <summary>
/// Process-level integration tests that launch the CosmosDBShell executable as a
/// separate child process and drive it through stdin/stdout. These tests verify
/// that end-to-end CLI wiring (argument parsing, stdin redirection, '>' file
/// redirection, exit codes) works outside the in-process interpreter harness.
/// </summary>
public class ShellProcessTests
{
    private static readonly Regex AnsiEscape = new("\x1b\\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    [Theory]
    [InlineData(false, "; ")]
    [InlineData(true, "; ")]
    [InlineData(true, "\n")]
    public async Task MultipleStatements_PrintEveryResultInOrder(bool stdin, string separator)
    {
        var command = $"echo first{separator}echo second";
        var result = await RunShellAsync(
            stdin ? command : null,
            extraArgs: stdin ? ["--quiet", "--output", "json"] : ["--quiet", "--output", "json", "-c", command],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}", result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task MultipleStatements_HonorEachFileRedirection(bool stdin, bool sameFile, bool append)
    {
        var firstFile = Path.GetTempFileName();
        var secondFile = sameFile ? firstFile : Path.GetTempFileName();
        try
        {
            var command = $"echo first > {JsonSerializer.Serialize(firstFile)}; echo second {(append ? ">>" : ">")} {JsonSerializer.Serialize(secondFile)}";
            var result = await RunShellAsync(
                stdin ? command : null,
                extraArgs: stdin ? ["--quiet", "--output", "json"] : ["--quiet", "--output", "json", "-c", command],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StdOut);
            Assert.Empty(result.StdErr);
            Assert.Equal(
                append ? $"first{Environment.NewLine}second{Environment.NewLine}" : $"{(sameFile ? "second" : "first")}{Environment.NewLine}",
                await File.ReadAllTextAsync(firstFile, TestContext.Current.CancellationToken));
            if (!sameFile)
            {
                Assert.Equal($"second{Environment.NewLine}", await File.ReadAllTextAsync(secondFile, TestContext.Current.CancellationToken));
            }
        }
        finally
        {
            File.Delete(firstFile);
            File.Delete(secondFile);
        }
    }

    [Theory]
    [InlineData(false, "echo second", 0, "second")]
    [InlineData(true, "echo second", 0, "second")]
    [InlineData(false, "not-a-real-command", 2, "")]
    [InlineData(true, "not-a-real-command", 2, "")]
    public async Task MultipleStatements_PreserveEarlierOutputAndDoNotLeakRedirect(bool stdin, string nextCommand, int exitCode, string stdout)
    {
        var file = Path.GetTempFileName();
        try
        {
            var command = $"echo first > {JsonSerializer.Serialize(file)}; {nextCommand}";
            var result = await RunShellAsync(
                stdin ? command : null,
                extraArgs: stdin ? ["--quiet", "--output", "json"] : ["--quiet", "--output", "json", "-c", command],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(exitCode, result.ExitCode);
            Assert.Equal("first" + Environment.NewLine, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            Assert.Equal(stdout.Length == 0 ? string.Empty : stdout + Environment.NewLine, result.StdOut);
            if (exitCode != 0)
            {
                using var error = JsonDocument.Parse(result.StdErr);
                Assert.Equal("error", error.RootElement.GetProperty("status").GetString());
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(">", "echo first\necho second")]
    [InlineData(">>", "echo first\necho second")]
    [InlineData(">", "{ echo first; echo second }")]
    [InlineData(">", "def show { echo first; echo second }\nshow")]
    [InlineData(">", "echo first\nreturn \"second\"\necho skipped")]
    public async Task WholeScriptRedirection_PreservesEveryStatement(string redirect, string content)
    {
        var script = Path.Join(AppContext.BaseDirectory, $"output-{Guid.NewGuid():N}.csh");
        var output = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(script, content, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(output, "before" + Environment.NewLine, TestContext.Current.CancellationToken);
            var result = await RunShellAsync(
                null,
                extraArgs: ["--quiet", "--output", "json", "-c", $"{Path.GetFileName(script)} {redirect} {JsonSerializer.Serialize(output)}"],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StdOut);
            Assert.Empty(result.StdErr);
            Assert.Equal(
                (redirect == ">>" ? "before" + Environment.NewLine : string.Empty) + $"first{Environment.NewLine}second{Environment.NewLine}",
                await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(script);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData("def empty { }; echo first; empty")]
    [InlineData("def empty { }; echo first | empty | echo")]
    [InlineData("def value { echo first }; value; value")]
    public async Task EmptyFunction_DoesNotRepeatRenderedOutput(string command)
    {
        var result = await RunShellAsync(
            null,
            extraArgs: ["--quiet", "--output", "json", "-c", command],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            command.StartsWith("def value", StringComparison.Ordinal)
                ? $"first{Environment.NewLine}first{Environment.NewLine}"
                : "first" + Environment.NewLine,
            result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EmptyScript_DoesNotRepeatRenderedOutput(bool stdin, bool redirect)
    {
        var script = Path.Join(AppContext.BaseDirectory, $"empty-{Guid.NewGuid():N}.csh");
        var output = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(script, string.Empty, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(output, "before", TestContext.Current.CancellationToken);
            var command = $"echo first; {Path.GetFileName(script)}";
            if (redirect)
            {
                command += $" > {JsonSerializer.Serialize(output)}";
            }

            var result = await RunShellAsync(
                stdin ? command : null,
                extraArgs: stdin ? ["--quiet", "--output", "json"] : ["--quiet", "--output", "json", "-c", command],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("first" + Environment.NewLine, result.StdOut);
            Assert.Empty(result.StdErr);
            Assert.Equal(redirect ? string.Empty : "before", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));

            var pipeline = await RunShellAsync(
                null,
                extraArgs: ["--quiet", "--output", "json", "-c", $"echo first | {Path.GetFileName(script)} | echo"],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, pipeline.ExitCode);
            Assert.Equal("first" + Environment.NewLine, pipeline.StdOut);
            Assert.Empty(pipeline.StdErr);
        }
        finally
        {
            File.Delete(script);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "2>")]
    [InlineData(true, "2>")]
    [InlineData(false, "2>>")]
    [InlineData(true, "2>>")]
    public async Task WholeScriptRedirection_MissingDirectory_ReportsErrorWithoutExecutingScript(bool stdin, string errorRedirect)
    {
        var script = Path.Join(AppContext.BaseDirectory, $"redirect-error-{Guid.NewGuid():N}.csh");
        var marker = Path.Join(AppContext.BaseDirectory, $"not-executed-{Guid.NewGuid():N}.txt");
        var output = Path.Join(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "out.txt");
        var errorFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                script, $"echo executed > {JsonSerializer.Serialize(marker)}", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(errorFile, "existing" + Environment.NewLine, TestContext.Current.CancellationToken);
            var command = $"{Path.GetFileName(script)} > {JsonSerializer.Serialize(output)}";
            if (errorRedirect.Length > 0)
            {
                command += $" {errorRedirect} {JsonSerializer.Serialize(errorFile)}";
            }

            var result = await RunShellAsync(
                stdin ? command : null,
                extraArgs: stdin ? ["--quiet", "--output", "json"] : ["--quiet", "--output", "json", "-c", command],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ShellExitCode.GeneralFailure, result.ExitCode);
            Assert.Empty(result.StdOut);
            Assert.False(File.Exists(marker));
            Assert.False(File.Exists(output));
            var diagnostic = result.StdErr;
            if (errorRedirect.Length > 0)
            {
                Assert.Empty(result.StdErr);
                diagnostic = await File.ReadAllTextAsync(errorFile, TestContext.Current.CancellationToken);
                if (errorRedirect == "2>>")
                {
                    Assert.StartsWith("existing" + Environment.NewLine, diagnostic);
                    diagnostic = diagnostic[("existing" + Environment.NewLine).Length..];
                }
            }

            using var error = JsonDocument.Parse(diagnostic);
            Assert.Equal("error", error.RootElement.GetProperty("status").GetString());
            Assert.Contains(output, error.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(script);
            File.Delete(marker);
            File.Delete(errorFile);
        }
    }

    [Theory]
    [InlineData("inline")]
    [InlineData("stdin")]
    [InlineData("script")]
    public async Task MultipleStatements_ParseFailure_DoesNotPrintOrTruncate(string input)
    {
        var script = Path.Join(AppContext.BaseDirectory, $"invalid-{Guid.NewGuid():N}.csh");
        var output = Path.GetTempFileName();
        try
        {
            var command = $"echo first > {JsonSerializer.Serialize(output)}; $x =";
            await File.WriteAllTextAsync(output, "before", TestContext.Current.CancellationToken);
            if (input == "script")
            {
                await File.WriteAllTextAsync(script, command, TestContext.Current.CancellationToken);
                command = $"{Path.GetFileName(script)} > {JsonSerializer.Serialize(output)}";
            }

            var result = await RunShellAsync(
                input == "stdin" ? command : null,
                extraArgs: input == "stdin" ? ["--quiet", "--output", "json"] : ["--quiet", "--output", "json", "-c", command],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ShellExitCode.UsageError, result.ExitCode);
            Assert.Empty(result.StdOut);
            Assert.NotEmpty(result.StdErr);
            Assert.Equal("before", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(script);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task MultipleStatements_ExpressionsAndPipelines_DoNotDuplicateOutput()
    {
        var result = await RunShellAsync(
            null,
            extraArgs: ["--quiet", "--output", "json", "-c", "$value = (echo first); echo $value | echo; echo second"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}", result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleStatements_ErrorRedirectionDoesNotLeak(bool stdin)
    {
        var file = Path.GetTempFileName();
        try
        {
            var command = $"echo first 2> {JsonSerializer.Serialize(file)}; not-a-real-command";
            var result = await RunShellAsync(
                stdin ? command : null,
                extraArgs: stdin ? ["--quiet", "--output", "json"] : ["--quiet", "--output", "json", "-c", command],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ShellExitCode.UsageError, result.ExitCode);
            Assert.Equal("first" + Environment.NewLine, result.StdOut);
            Assert.Empty(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            using var error = JsonDocument.Parse(result.StdErr);
            Assert.Equal("error", error.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("{\"name\":\"missing-id\"}")]
    [InlineData("{\"id\":42}")]
    public async Task ImportDryRun_InvalidId_ReturnsUsageExitCodeAndOfflineResult(string content)
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);
            var result = await RunShellAsync(
                stdinScript: null,
                extraArgs: ["--output", "json", "-c", $"import {JsonSerializer.Serialize(file)} --dry-run"],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ShellExitCode.UsageError, result.ExitCode);
            Assert.Empty(result.StdOut);
            using var error = JsonDocument.Parse(result.StdErr);
            var import = error.RootElement.GetProperty("result");
            Assert.Equal(0, import.GetProperty("imported").GetInt32());
            Assert.Equal(0, import.GetProperty("validated").GetInt32());
            Assert.Equal("offline", import.GetProperty("validationScope").GetString());
            Assert.False(import.GetProperty("targetValidated").GetBoolean());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("value", "identity", "1 argument, got 0")]
    [InlineData("value", "identity 1 2", "1 argument, got 2")]
    [InlineData("value", "$result = (identity)", "1 argument, got 0")]
    [InlineData("", "identity 1", "0 arguments, got 1")]
    [InlineData("first second", "identity", "2 arguments, got 0")]
    public async Task WrongFunctionArgumentCount_ReturnsUsageExitCode(string parameters, string invocation, string expectedMessage)
    {
        var result = await RunShellAsync($"def identity [{parameters}] {{ return }}; {invocation}", cancellationToken: TestContext.Current.CancellationToken, extraArgs: ["--quiet"]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("expects " + expectedMessage, result.StdErr);
    }

    [Fact]
    public async Task DeepExpression_FailsBeforeExecution_WithoutCrashingProcess()
    {
        var script = "echo BEFORE_DEPTH_ERROR; $value = " + string.Join(" + ", Enumerable.Repeat("1", 10001));
        var result = await RunShellAsync(script, cancellationToken: TestContext.Current.CancellationToken, extraArgs: ["--quiet"]);
        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain("BEFORE_DEPTH_ERROR", result.StdOut);
        Assert.Contains("expression tree depth", result.StdErr);
        Assert.DoesNotContain("Stack overflow", result.StdErr);
    }

    [Fact]
    public async Task ExpressionAtDepthLimit_EvaluatesSuccessfully()
    {
        var script = "$value = " + string.Join(" + ", Enumerable.Repeat("1", 128)) + "; echo $value";
        var result = await RunShellAsync(script, cancellationToken: TestContext.Current.CancellationToken, extraArgs: ["--quiet"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("128", result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Theory]
    [InlineData("doctor --no-update-check --format json", 0, "PASS")]
    [InlineData("doctor --database missing --no-update-check --format json", 1, "FAIL")]
    [InlineData("doctor --arm --no-update-check --format json", 1, "FAIL")]
    public async Task Doctor_ReportsJsonAndRequiredCheckExitCode(string command, int exitCode, string status)
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--output", "json", "-c", command],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(exitCode, result.ExitCode);
        using var report = JsonDocument.Parse(result.StdOut);
        Assert.Equal(1, report.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(status, report.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task StdinPipedScript_VersionCommand_PrintsVersionLineAndExitsZero()
    {
        var result = await RunShellAsync("version", TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Cosmos Shell version:", result.StdOut);
    }

    [Fact]
    public async Task StdinPipedScript_EchoCommand_WritesArgumentToStdOut()
    {
        var result = await RunShellAsync("echo \"hello from process\"", TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from process", result.StdOut);
    }

    [Fact]
    public async Task InvariantGlobalization_ExecuteCommand_LoadsEnglishCatalog()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--quiet", "-c", "def identity [value] { return }; identity"],
            cancellationToken: TestContext.Current.CancellationToken,
            environment: new Dictionary<string, string?>
            {
                ["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1",
            });

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("expects 1 argument, got 0", result.StdErr);
        Assert.DoesNotContain("1 arguments", result.StdErr);
    }

    [Fact]
    public async Task StdinPipedScript_MultipleCommands_AllRunAndLastOutputVisible()
    {
        // Pipe several commands in one stdin script. The final command's stdout must
        // make it through, which proves the full script was parsed and executed.
        var script = "echo \"first\";echo \"second\";echo \"TAIL_MARKER\"";

        var result = await RunShellAsync(script, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TAIL_MARKER", result.StdOut);
    }

    [Fact]
    public async Task ProcessStdOut_CanBePipedToFile()
    {
        // Emulates 'cosmosdbshell < script.txt > output.txt': the caller captures the
        // process's stdout and writes it to a file, then verifies the command ran.
        var outFile = Path.Combine(Path.GetTempPath(), $"cosmosshell-proc-{Guid.NewGuid():N}.txt");
        try
        {
            var result = await RunShellAsync("echo \"PIPE_SMOKE_TEST\"", TestContext.Current.CancellationToken);

            Assert.Equal(0, result.ExitCode);
            await File.WriteAllTextAsync(outFile, result.StdOut, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(outFile));
            var content = await File.ReadAllTextAsync(outFile, TestContext.Current.CancellationToken);
            Assert.Contains("PIPE_SMOKE_TEST", content);
        }
        finally
        {
            if (File.Exists(outFile))
            {
                File.Delete(outFile);
            }
        }
    }

    [Fact]
    public async Task StdinPipedScript_UnknownCommand_ReturnsNonZeroExitCode()
    {
        var result = await RunShellAsync("no_such_command_xyz_12345", TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAndQuit_Option_RunsSingleCommandAndExitsZero()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["-c", "echo \"quit-after-this\""],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("quit-after-this", result.StdOut);
    }

    [Theory]
    [InlineData("$name")]
    [InlineData("$(echo injected)")]
    public async Task ExecuteAndQuit_DoubleQuotedDollarSyntax_RemainsLiteral(string value)
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["-c", $"echo \"{value}\""],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(value, result.StdOut, StringComparison.Ordinal);
        Assert.Empty(result.StdErr.Trim());
    }

    [Theory]
    [InlineData("$name")]
    [InlineData("$(echo injected)")]
    public async Task ExecuteAndQuit_InterpolatedStringEscapedDollar_RemainsLiteral(string value)
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["-c", $"echo $\"literal \\{value}\""],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"literal {value}", result.StdOut, StringComparison.Ordinal);
        Assert.Empty(result.StdErr.Trim());
    }

    [Fact]
    public async Task VersionOption_PrintsVersionAndExitsZero()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--version"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CosmosDBShell", result.StdOut);
    }

    [Fact]
    public async Task HelpOption_PrintsHelpAndExitsZero()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--help"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--connect", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--database <id>", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--container <id>", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--version", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartupContainer_WithoutDatabase_ReturnsUsageExitCode()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--container", "items"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("requires '--database'", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartupDatabase_WithoutConnect_ReturnsUsageExitCode()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--database", "samples"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("require '--connect'", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownRootArgument_ReturnsUsageExitCode()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["not-a-root-option"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unrecognized argument 'not-a-root-option'", result.StdOut + result.StdErr);
        Assert.DoesNotContain("Option '--connect-mode' is defined with a bad format", result.StdOut + result.StdErr);
    }

    [Fact]
    public async Task MissingConnectValue_ReportsUserFacingOptionAlias()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--connect"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Option '--connect' has no value.", result.StdOut + result.StdErr);
    }

    [Fact]
    [Trait("Category", "Emulator")]
    public async Task ConnectOption_WithExecuteAndQuit_RunsCommandAgainstEmulator()
    {
        await EmulatorProbe.EnsureAvailableAsync();
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorTestBase.EmulatorEndpoint);

        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--connect", connectionString, "--output", "table", "-c", "connect"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Connected to account", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localhost", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Current Location", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Emulator")]
    public async Task StartupNavigation_WithDatabaseAndContainer_StartsAtRequestedContainer()
    {
        await EmulatorProbe.EnsureAvailableAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorTestBase.EmulatorEndpoint);
        var databaseName = $"StartupNav_{Guid.NewGuid():N}";
        const string containerName = "RequestedContainer";
        var options = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            ServerCertificateCustomValidationCallback = (_, _, _) => true,
        };

        using var client = new CosmosClient(connectionString, options);
        var database = (await client.CreateDatabaseAsync(databaseName, cancellationToken: cancellationToken)).Database;
        try
        {
            await database.CreateContainerAsync(containerName, "/id", cancellationToken: cancellationToken);

            var result = await RunShellAsync(
                stdinScript: null,
                extraArgs: [
                    "--connect", connectionString,
                    "--database", databaseName,
                    "--container", containerName,
                    "--output", "json",
                    "-c", "pwd",
                ],
                cancellationToken: cancellationToken);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StdErr.Trim());
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.Equal(databaseName, document.RootElement.GetProperty("database").GetString());
            Assert.Equal(containerName, document.RootElement.GetProperty("container").GetString());
            Assert.Equal($"/{databaseName}/{containerName}", document.RootElement.GetProperty("currentLocation").GetString());
        }
        finally
        {
            await database.DeleteAsync(cancellationToken: cancellationToken);
        }
    }

    [Fact]
    public async Task BadArguments_WithOutputJson_ReturnsExitCode2_AndJsonOnStdErr()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--output", "json", "-c", "not a real command"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StdOut.Trim());
        Assert.Contains("\"status\":\"error\"", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not recognized as an internal or external command", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectionFailure_WithOutputJson_ReturnsExitCode4_AndJsonOnStdErr()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--connect", "https://127.0.0.1:1", "--output", "json", "-c", "version"],
            cancellationToken: TestContext.Current.CancellationToken);

        // Connection/network failures map to ShellExitCode.ConnectionError (4).
        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.StdOut.Trim());
        Assert.Contains("\"status\":\"error\"", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StdinPipedScript_SyntaxError_ReturnsUsageExitCode()
    {
        var result = await RunShellAsync("echo \"unterminated", TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task MalformedOtelEndpoint_ReturnsUsageExitCode()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--otel", "not-a-valid-endpoint", "-c", "version"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task SlashC_WithMalformedOtelEndpoint_ReturnsJsonError()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--otel", "not-a-valid-endpoint", "/c", "version"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StdOut.Trim());
        Assert.Contains("\"status\":\"error\"", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAndQuit_WithoutOutput_EmitsParseableJsonOnStdOut()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["-c", "version"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StdErr.Trim());

        using var doc = JsonDocument.Parse(result.StdOut);
        Assert.True(doc.RootElement.ValueKind == JsonValueKind.Object || doc.RootElement.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task InvalidMcpPort_WithOutputJson_ReturnsExitCode2_AndJsonOnStdErr()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--output", "json", "--mcp", "0"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StdOut.Trim());
        Assert.Contains("\"status\":\"error\"", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("port", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClearHistory_WithOutputJson_DoesNotWriteInformationalStdOut()
    {
        var result = await RunShellAsync(
            stdinScript: null,
            extraArgs: ["--output", "json", "--clear-history"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StdOut.Trim());
    }

    [Fact]
    public async Task ClearHistory_WhenHistoryFileIsLocked_ReturnsFailure()
    {
        var configDir = Path.Join(Path.GetTempPath(), $"cosmosshell-clear-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);
        var historyFile = Path.Join(configDir, "cmd_history");
        await File.WriteAllTextAsync(historyFile, "echo retained", TestContext.Current.CancellationToken);
        try
        {
            using var lockedStream = new FileStream(
                historyFile,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            var result = await RunShellAsync(
                stdinScript: null,
                extraArgs: ["--output", "json", "--clear-history"],
                cancellationToken: TestContext.Current.CancellationToken,
                configDirectory: configDir);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.StdOut.Trim());
            Assert.Contains("\"status\":\"error\"", result.StdErr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Failed to clear history", result.StdErr);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    private static async Task<ShellProcessResult> RunShellAsync(
        string stdinScript,
        CancellationToken cancellationToken)
    {
        return await RunShellAsync(stdinScript, extraArgs: null, cancellationToken);
    }

    private static async Task<ShellProcessResult> RunShellAsync(
        string? stdinScript,
        IEnumerable<string>? extraArgs,
        CancellationToken cancellationToken,
        string? configDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var argsList = extraArgs?.ToList();
        var requiresOwnedStdin = stdinScript != null
            || (argsList?.Contains("-c") == true)
            || (argsList?.Contains("-k") == true);

        var shellDll = Path.Combine(AppContext.BaseDirectory, "CosmosDBShell.dll");
        if (!File.Exists(shellDll))
        {
            throw SkipException.ForSkip($"CosmosDBShell.dll not found next to test assembly at '{shellDll}'.");
        }

        var dotnet = GetDotnetPath();

        // Isolate the shell's config directory so process-level tests (for example
        // --clear-history) never touch the developer's real command history under
        // %LocalAppData%\CosmosDBShell.
        var ownsConfigDirectory = configDirectory == null;
        var isolatedConfigDir = configDirectory
            ?? Path.Join(Path.GetTempPath(), $"cosmosshell-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolatedConfigDir);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardInput = requiresOwnedStdin,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add(shellDll);
            if (extraArgs != null)
            {
                foreach (var a in extraArgs)
                {
                    startInfo.ArgumentList.Add(a);
                }
            }

            // Keep the environment minimal and predictable.
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            startInfo.Environment.Remove("COSMOSDB_SHELL_FORMAT");
            startInfo.Environment["COSMOSDB_SHELL_CONFIG_DIR"] = isolatedConfigDir;
            if (environment != null)
            {
                foreach (var (key, value) in environment)
                {
                    if (value == null)
                    {
                        startInfo.Environment.Remove(key);
                    }
                    else
                    {
                        startInfo.Environment[key] = value;
                    }
                }
            }

            using var process = new Process { StartInfo = startInfo };
            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stdOut.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stdErr.AppendLine(e.Data);
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start CosmosDBShell process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (requiresOwnedStdin)
            {
                if (stdinScript != null)
                {
                    await process.StandardInput.WriteAsync(stdinScript);
                    await process.StandardInput.WriteLineAsync();
                }

                process.StandardInput.Close();
            }

            // Enforce a reasonable timeout so a hang does not block the test run indefinitely.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);

                // WaitForExitAsync can return before the async output/error events
                // have finished draining; the synchronous wait flushes them so the
                // captured StdOut/StdErr are complete before we assert on them.
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Process may already have exited or cannot be killed on this platform.
                }

                throw new TimeoutException(
                    $"CosmosDBShell process did not exit in time. StdOut: {Strip(stdOut.ToString())} StdErr: {Strip(stdErr.ToString())}");
            }

            return new ShellProcessResult(
                process.ExitCode,
                Strip(stdOut.ToString()),
                Strip(stdErr.ToString()));
        }
        finally
        {
            if (ownsConfigDirectory)
            {
                try
                {
                    Directory.Delete(isolatedConfigDir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup; the OS temp directory is reclaimed eventually.
                }
            }
        }
    }

    private static string GetDotnetPath()
    {
        // Inside 'dotnet test', DOTNET_HOST_PATH points at the active dotnet executable.
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(dotnet) && File.Exists(dotnet))
        {
            return dotnet;
        }

        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    private static string Strip(string text)
    {
        // Strip ANSI color/control sequences so asserts don't depend on terminal capabilities.
        return AnsiEscape.Replace(text, string.Empty);
    }

    private sealed record ShellProcessResult(int ExitCode, string StdOut, string StdErr);
}
