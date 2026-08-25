// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Util;

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
        Assert.Contains("--version", result.StdOut, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("Required option '--connect' is missing", result.StdOut + result.StdErr);
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

    private static async Task<ShellProcessResult> RunShellAsync(
        string stdinScript,
        CancellationToken cancellationToken)
    {
        return await RunShellAsync(stdinScript, extraArgs: null, cancellationToken);
    }

    private static async Task<ShellProcessResult> RunShellAsync(
        string? stdinScript,
        IEnumerable<string>? extraArgs,
        CancellationToken cancellationToken)
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
        var isolatedConfigDir = Path.Join(Path.GetTempPath(), $"cosmosshell-test-{Guid.NewGuid():N}");
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
