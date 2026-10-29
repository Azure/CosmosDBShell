// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;

public class OperationInactivityTimeoutTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task WithoutProgress_CancelsAfterTimeout()
    {
        using var source = new CancellationTokenSource();
        _ = new OperationInactivityTimeout(source, Timeout);

        await WaitForCancellationAsync(source);
    }

    [Fact]
    public async Task Progress_RestartsTimeout()
    {
        var timeout = TimeSpan.FromSeconds(1);
        using var source = new CancellationTokenSource();
        var deadline = new OperationInactivityTimeout(source, timeout);

        // Four 400 ms gaps exceed the timeout in total while leaving slack for a busy machine.
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);
            deadline.ReportProgress();
        }

        Assert.False(source.IsCancellationRequested);
        await WaitForCancellationAsync(source);
    }

    [Fact]
    public async Task NestedPauses_SuspendUntilLastResume_AndIgnoreProgress()
    {
        using var source = new CancellationTokenSource();
        var deadline = new OperationInactivityTimeout(source, Timeout);

        var outer = deadline.Pause();
        var inner = deadline.Pause();
        deadline.ReportProgress();
        await Task.Delay(Timeout * 3, TestContext.Current.CancellationToken);
        Assert.False(source.IsCancellationRequested);

        inner.Dispose();
        inner.Dispose();
        await Task.Delay(Timeout * 3, TestContext.Current.CancellationToken);
        Assert.False(source.IsCancellationRequested);

        outer.Dispose();
        await WaitForCancellationAsync(source);
    }

    [Fact]
    public void ProgressAndPause_AfterSourceDisposed_DoNotThrow()
    {
        var source = new CancellationTokenSource();
        var deadline = new OperationInactivityTimeout(source, Timeout);
        source.Dispose();

        deadline.ReportProgress();
        deadline.Pause().Dispose();
    }

    [Fact]
    public void ShellWithoutRunningCommand_PauseIsNoOp()
    {
        using var shell = ShellInterpreter.CreateInstance();

        shell.PauseOperationTimeout().Dispose();
        shell.ReportOperationProgress();
    }

    private static async Task WaitForCancellationAsync(CancellationTokenSource source)
    {
        await Task.Delay(Timeout * 10, source.Token).ContinueWith(
            _ => { },
            TestContext.Current.CancellationToken,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        Assert.True(source.IsCancellationRequested, "The inactivity timeout did not cancel the operation.");
    }
}
