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
    [InlineData(null, null, false, false, false, true)]
    [InlineData("user", null, false, false, false, true)]
    [InlineData("table", null, false, false, false, true)]
    [InlineData("json", null, false, false, false, false)]
    [InlineData("csv", null, false, false, false, false)]
    [InlineData(null, "json", false, false, false, false)]
    [InlineData(null, "csv", false, false, false, false)]
    [InlineData("json", "user", false, false, false, false)]
    [InlineData("user", null, true, false, false, false)]
    [InlineData("user", null, false, true, false, false)]
    [InlineData("user", null, false, false, true, false)]
    public void OutputPolicy(
        string? globalFormat, string? localFormat, bool quiet, bool mcp, bool redirected, bool expected)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = globalFormat, Quiet = quiet };
        shell.IsMcpInvocation = mcp;
        shell.StdOutRedirect = redirected ? "result.json" : null;
        var state = new CommandState();
        state.SetFormat(localFormat);

        Assert.Equal(expected, IngestionProgress.IsEnabled(shell, state));
    }

    [Theory]
    [InlineData("command-import-progress", "3 items processed", "2 imported", "1 failed", "items/s")]
    [InlineData("command-bulk-progress", "3 of 10 operations processed", "2 succeeded", "1 failed", "operations/s")]
    [InlineData("command-import-validation-progress", "3 items validated offline", "no writes sent", "Dry run:", "items/s")]
    public async Task ReportsWhileWorkIsStalledAndStopsBeforeReturning(
        string key, string count, string succeeded, string failed, string rate)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "user" };
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new CompletingWriter(completion);
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

            Assert.Equal(1, output.Lines);
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
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "user" };
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

    private sealed class CompletingWriter(TaskCompletionSource completion) : StringWriter
    {
        internal int Lines { get; private set; }

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            this.Lines++;
            completion.TrySetResult();
        }
    }
}
