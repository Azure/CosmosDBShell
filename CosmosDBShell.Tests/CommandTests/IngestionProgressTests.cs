// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

[Collection(ConsoleOutputTestCollection.Name)]
public class IngestionProgressTests
{
    [Theory]
    [InlineData(null, null, false, false, false, true, true)]
    [InlineData("user", null, false, false, false, true, true)]
    [InlineData("table", null, false, false, false, true, true)]
    [InlineData("json", null, false, false, false, true, false)]
    [InlineData("csv", null, false, false, false, true, false)]
    [InlineData(null, "json", false, false, false, true, false)]
    [InlineData(null, "csv", false, false, false, true, false)]
    [InlineData("json", "user", false, false, false, true, false)]
    [InlineData("user", null, true, false, false, true, false)]
    [InlineData("user", null, false, true, false, true, false)]
    [InlineData("user", null, false, false, true, true, false)]
    [InlineData("user", null, false, false, false, false, false)]
    [InlineData(null, null, false, false, false, false, false)]
    public void OutputPolicy(
        string? globalFormat, string? localFormat, bool quiet, bool mcp, bool redirected, bool terminal, bool expected)
    {
        // The documented environment default would otherwise change the cases without a format.
        var previousFormat = Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT");
        Environment.SetEnvironmentVariable("COSMOSDB_SHELL_FORMAT", null);
        try
        {
            using var shell = ShellInterpreter.CreateInstance();
            shell.Options = new Program.CosmosShellOptions { Output = globalFormat, Quiet = quiet };
            shell.IsMcpInvocation = mcp;
            shell.StdOutRedirect = redirected ? "result.json" : null;
            shell.IsOutputTerminal = () => terminal;
            var state = new CommandState();
            state.SetFormat(localFormat);

            Assert.Equal(expected, IngestionProgress.IsEnabled(shell, state));
        }
        finally
        {
            Environment.SetEnvironmentVariable("COSMOSDB_SHELL_FORMAT", previousFormat);
        }
    }

    [Theory]
    [InlineData("command-import-progress", "3 items processed", "2 imported", "1 failed", "items/s")]
    [InlineData("command-bulk-progress", "3 of 10 operations processed", "2 succeeded", "1 failed", "operations/s")]
    [InlineData("command-import-validation-progress", "3 items validated offline", "no writes sent", "Dry run:", "items/s")]
    [InlineData("command-rm-progress", "3 items scanned", "2 deleted", "Remove:", "RU charge")]
    [InlineData("command-rm-dry-run-progress", "3 items scanned", "2 would be deleted", "Dry run:", "RU charge")]
    public async Task ReportsWhileWorkIsStalledAndStopsBeforeReturning(
        string key, string count, string succeeded, string failed, string rate)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var shell = CreateTerminalShell();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new CompletingWriter(completion, completeOnWrite: true);
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            var reporter = new IngestionProgress(shell, new CommandState(), key, 10);
            reporter.Update(3, 2, 1, 7.5);

            await reporter.RunAsync(() => completion.Task.WaitAsync(timeout.Token));

            var text = output.ToString();
            Assert.Contains(count, text);
            Assert.Contains(succeeded, text);
            Assert.Contains(failed, text);
            Assert.Contains("elapsed", text);
            Assert.Contains(rate, text);
            Assert.DoesNotContain("{ $", text);
            if (key != "command-import-validation-progress")
            {
                Assert.Contains("7.50", text);
            }
        }
        finally
        {
            completion.TrySetResult();
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task FastWorkIsSilentAndFailuresAndCancellationPropagate()
    {
        using var shell = CreateTerminalShell();
        using var output = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            var reporter = new IngestionProgress(shell, new CommandState(), "command-import-progress");
            await reporter.RunAsync(() => Task.CompletedTask);
            var failure = new IOException("input failed");
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(
                () => reporter.RunAsync(() => Task.FromException(failure))));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => reporter.RunAsync(() => Task.FromCanceled(new CancellationToken(true))));
            Assert.Empty(output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportingFailure_DoesNotChangeOperationOutcome(bool operationFails)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var shell = CreateTerminalShell();
        var writeAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new FailingWriter(writeAttempted);
        var original = Console.Out;
        var operationFailure = new InvalidOperationException("write failed");
        try
        {
            Console.SetOut(output);
            var reporter = new IngestionProgress(shell, new CommandState(), "command-import-progress");

            async Task OperationAsync()
            {
                await writeAttempted.Task.WaitAsync(timeout.Token);
                if (operationFails)
                {
                    throw operationFailure;
                }
            }

            if (operationFails)
            {
                Assert.Same(operationFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => reporter.RunAsync(OperationAsync)));
            }
            else
            {
                await reporter.RunAsync(OperationAsync);
            }

            Assert.True(writeAttempted.Task.IsCompleted);
        }
        finally
        {
            writeAttempted.TrySetResult();
            Console.SetOut(original);
        }
    }
    [Fact]
    public async Task Terminal_RedrawsOneLineInPlaceAndClearsItBeforeReturning()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "user" };
        shell.IsOutputTerminal = () => true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new CompletingWriter(completion, completeOnWrite: true);
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            var reporter = new IngestionProgress(shell, new CommandState(), "command-import-progress");
            reporter.Update(3, 2, 1, 7.5);

            await reporter.RunAsync(() => completion.Task.WaitAsync(timeout.Token));

            var text = output.ToString();
            Assert.StartsWith("\rImport: 3 items processed", text);
            Assert.DoesNotContain('\n', text);
            Assert.Equal(0, output.Lines);
            var drawn = text.IndexOf('\r', 1);
            Assert.Equal("\r" + new string(' ', drawn - 1) + "\r", text[drawn..]);
        }
        finally
        {
            completion.TrySetResult();
            Console.SetOut(original);
        }
    }

    [Fact]
    public void TransientLine_PadsShorterTextAndClearsOnce()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var output = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            shell.WriteTransientLine("abcdef");
            shell.WriteTransientLine("xy");
            shell.ClearTransientLine();
            shell.ClearTransientLine();

            // The padding already blanks the longer text, so clearing covers only "xy".
            Assert.Equal("\rabcdef\rxy    \r  \r", output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static ShellInterpreter CreateTerminalShell()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "user" };
        shell.IsOutputTerminal = () => true;
        return shell;
    }

    private sealed class FailingWriter(TaskCompletionSource attempted) : StringWriter
    {
        public override void Write(string? value)
        {
            attempted.TrySetResult();
            throw new IOException("console unavailable");
        }
    }

    private sealed class CompletingWriter(TaskCompletionSource completion, bool completeOnWrite = false) : StringWriter
    {
        internal int Lines { get; private set; }

        public override void Write(string? value)
        {
            base.Write(value);
            if (completeOnWrite)
            {
                completion.TrySetResult();
            }
        }

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            this.Lines++;
            completion.TrySetResult();
        }
    }
}
