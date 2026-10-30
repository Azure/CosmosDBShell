// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Cancels a command after a period without progress. Progress restarts the period, and
/// pauses (for example while waiting for user input) suspend it until every pause ends.
/// </summary>
internal sealed class OperationInactivityTimeout : IDisposable
{
    internal static readonly IDisposable NoPause = new EmptyScope();

    private readonly object sync = new();
    private readonly CancellationTokenSource source;
    private readonly CancellationToken callerToken;
    private readonly TimeSpan timeout;
    private readonly ITimer? timer;
    private int pauseCount;

    /// <param name="source">The source canceled when the timeout expires.</param>
    /// <param name="timeout">The period without progress that cancels <paramref name="source"/>.</param>
    /// <param name="callerToken">The caller's token; its cancellation is not reported as a timeout.</param>
    /// <param name="timeProvider">A clock for tests. When <see langword="null"/>, the source's own timer is used.</param>
    internal OperationInactivityTimeout(CancellationTokenSource source, TimeSpan timeout, CancellationToken callerToken = default, TimeProvider? timeProvider = null)
    {
        this.source = source;
        this.callerToken = callerToken;
        this.timeout = timeout;
        this.timer = timeProvider?.CreateTimer(
            static state => ((OperationInactivityTimeout)state!).Expire(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        this.Restart(timeout);
    }

    internal void ThrowIfTimedOut()
    {
        if (!this.callerToken.IsCancellationRequested)
        {
            this.source.Token.ThrowIfCancellationRequested();
        }
    }

    internal void ReportProgress()
    {
        lock (this.sync)
        {
            if (this.pauseCount == 0)
            {
                this.Restart(this.timeout);
            }
        }
    }

    internal IDisposable Pause()
    {
        lock (this.sync)
        {
            if (this.pauseCount++ == 0)
            {
                this.Restart(Timeout.InfiniteTimeSpan);
            }
        }

        return new PauseScope(this);
    }

    private void Resume()
    {
        lock (this.sync)
        {
            if (--this.pauseCount == 0)
            {
                this.Restart(this.timeout);
            }
        }
    }

    public void Dispose() => this.timer?.Dispose();

    private void Expire()
    {
        try
        {
            this.source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The command finished just before the timer fired.
        }
    }

    private void Restart(TimeSpan delay)
    {
        try
        {
            if (this.timer is not null)
            {
                this.timer.Change(delay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                this.source.CancelAfter(delay);
            }
        }
        catch (ObjectDisposedException)
        {
            // The command finished while a late completion was reporting progress.
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class PauseScope(OperationInactivityTimeout owner) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 0)
            {
                owner.Resume();
            }
        }
    }
}
