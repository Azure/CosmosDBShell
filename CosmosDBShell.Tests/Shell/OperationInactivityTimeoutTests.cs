// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class OperationInactivityTimeoutTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void WithoutProgress_CancelsAfterTimeout()
    {
        var clock = new ManualTimeProvider();
        using var source = new CancellationTokenSource(global::System.Threading.Timeout.InfiniteTimeSpan, clock);
        _ = new OperationInactivityTimeout(source, Timeout);

        clock.Advance(Timeout - TimeSpan.FromMilliseconds(1));
        Assert.False(source.IsCancellationRequested);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public void Progress_RestartsTimeout()
    {
        var clock = new ManualTimeProvider();
        using var source = new CancellationTokenSource(global::System.Threading.Timeout.InfiniteTimeSpan, clock);
        var deadline = new OperationInactivityTimeout(source, Timeout);

        // Three gaps of 0.9x the timeout exceed it in total, but each restarts the period.
        for (var i = 0; i < 3; i++)
        {
            clock.Advance(Timeout * 0.9);
            deadline.ReportProgress();
        }

        Assert.False(source.IsCancellationRequested);
        clock.Advance(Timeout);
        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public void NestedPauses_SuspendUntilLastResume_AndIgnoreProgress()
    {
        var clock = new ManualTimeProvider();
        using var source = new CancellationTokenSource(global::System.Threading.Timeout.InfiniteTimeSpan, clock);
        var deadline = new OperationInactivityTimeout(source, Timeout);

        var outer = deadline.Pause();
        var inner = deadline.Pause();
        deadline.ReportProgress();
        clock.Advance(Timeout * 3);
        Assert.False(source.IsCancellationRequested);

        inner.Dispose();
        inner.Dispose();
        clock.Advance(Timeout * 3);
        Assert.False(source.IsCancellationRequested);

        outer.Dispose();
        clock.Advance(Timeout - TimeSpan.FromMilliseconds(1));
        Assert.False(source.IsCancellationRequested);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(source.IsCancellationRequested);
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

    [Fact]
    public void EmulatorTimeout_KeepsScriptFramesOutermostAndClassifiesAsConnectionError()
    {
        var canceled = new OperationCanceledException("The operation was canceled.");
        var inner = new PositionalException("inner.csh", canceled, 3, 1, "bulk run plan.jsonl --yes");
        var outer = new PositionalException("outer.csh", inner, 7, 5, "inner.csh");

        var timeout = ShellInterpreter.CreateEmulatorTimeoutException(outer);

        var frames = PositionalException.GetSourceTrace(timeout);
        Assert.Equal(["inner.csh", "outer.csh"], frames.Select(frame => frame.FileName));
        Assert.Equal([3, 7], frames.Select(frame => frame.Line));
        Assert.IsType<PositionalException>(timeout);
        Assert.Contains("timed out", timeout.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ShellExitCode.ConnectionError, ShellExitCode.FromException(timeout));
        Assert.Same(canceled, timeout.InnerException!.InnerException!.InnerException!.InnerException);
    }

    [Fact]
    public void EmulatorTimeout_WithoutScriptFrame_ClassifiesAsConnectionError()
    {
        var timeout = ShellInterpreter.CreateEmulatorTimeoutException(new OperationCanceledException("The operation was canceled."));

        Assert.IsType<ShellException>(timeout);
        Assert.IsType<TimeoutException>(timeout.InnerException);
        Assert.Contains("timed out", timeout.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ShellExitCode.ConnectionError, ShellExitCode.FromException(timeout));
    }

    /// <summary>
    /// A deterministic clock: timers fire only when <see cref="Advance"/> moves time past them.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        private TimeSpan now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            this.timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            this.now += delta;
            foreach (var timer in this.timers.ToArray().Where(timer => timer.DueAt is { } due && due <= this.now))
            {
                timer.DueAt = null;
                timer.Fire();
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public TimeSpan? DueAt { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                this.DueAt = dueTime == global::System.Threading.Timeout.InfiniteTimeSpan ? null : owner.now + dueTime;
                return true;
            }

            public void Fire() => callback(state);

            public void Dispose() => this.DueAt = null;

            public ValueTask DisposeAsync()
            {
                this.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
