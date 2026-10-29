// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Cancels a command after a period without progress. Progress restarts the period, and
/// pauses (for example while waiting for user input) suspend it until every pause ends.
/// </summary>
internal sealed class OperationInactivityTimeout
{
    internal static readonly IDisposable NoPause = new EmptyScope();

    private readonly object sync = new();
    private readonly CancellationTokenSource source;
    private readonly CancellationToken callerToken;
    private readonly TimeSpan timeout;
    private int pauseCount;

    internal OperationInactivityTimeout(CancellationTokenSource source, TimeSpan timeout, CancellationToken callerToken = default)
    {
        this.source = source;
        this.callerToken = callerToken;
        this.timeout = timeout;
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

    private void Restart(TimeSpan delay)
    {
        try
        {
            this.source.CancelAfter(delay);
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
