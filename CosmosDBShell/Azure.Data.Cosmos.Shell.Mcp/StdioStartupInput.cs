// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.IO.Pipelines;

internal sealed class StdioStartupInput : IDisposable
{
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    private readonly Stream input;
    private readonly Pipe buffer = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource inputEnded = new();
    private readonly CancellationTokenSource deadline = new();
    private readonly CancellationTokenSource startupCancellation;
    private readonly Timer deadlineTimer;
    private Task pump = Task.CompletedTask;

    private Exception? inputError;
    private int startupTermination;
    private int disposed;

    public StdioStartupInput(Stream input, TimeSpan? timeout = null)
    {
        this.input = input;
        this.Input = this.buffer.Reader.AsStream();
        this.startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            this.inputEnded.Token, this.deadline.Token);
        this.StartupToken = this.startupCancellation.Token;
        this.deadlineTimer = new Timer(
            static state => ((StdioStartupInput)state!).CancelForTimeout(),
            this,
            timeout ?? StartupTimeout,
            Timeout.InfiniteTimeSpan);
    }

    private enum StartupTermination
    {
        None,
        TimedOut,
        InputEnded,
        InputError,
    }

    public Stream Input { get; }

    public CancellationToken StartupToken { get; }

    public Exception? InputError => Volatile.Read(ref this.startupTermination) == (int)StartupTermination.InputError
        ? Volatile.Read(ref this.inputError)
        : null;

    public bool TimedOut => Volatile.Read(ref this.startupTermination) == (int)StartupTermination.TimedOut;

    public void Start() => this.pump = this.PumpAsync();

    public void CompleteStartup() => this.deadlineTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        this.stopping.Cancel();
        this.Input.Dispose();
        this.input.Dispose();

        // Console pipe reads may not be interruptible. Do not hold process shutdown
        // open waiting for a read, or dispose its token sources while it is still running.
        _ = this.pump.ContinueWith(
            _ =>
            {
                this.startupCancellation.Dispose();
                this.deadline.Dispose();
                this.deadlineTimer.Dispose();
                this.inputEnded.Dispose();
                this.stopping.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task PumpAsync()
    {
        Exception? error = null;
        try
        {
            await this.input.CopyToAsync(
                this.buffer.Writer.AsStream(leaveOpen: true), 4096, this.stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (this.stopping.IsCancellationRequested)
        {
            // Disposal cancels the input pump; the pipe is completed in the finally block.
        }
        catch (Exception ex)
        {
            error = ex;
            Volatile.Write(ref this.inputError, ex);
            Interlocked.CompareExchange(
                ref this.startupTermination,
                (int)StartupTermination.InputError,
                (int)StartupTermination.None);
        }
        finally
        {
            await this.buffer.Writer.CompleteAsync(error).ConfigureAwait(false);
            if (!this.stopping.IsCancellationRequested)
            {
                Interlocked.CompareExchange(
                    ref this.startupTermination,
                    (int)StartupTermination.InputEnded,
                    (int)StartupTermination.None);
                await this.inputEnded.CancelAsync().ConfigureAwait(false);
            }
        }
    }

    private void CancelForTimeout()
    {
        if (Interlocked.CompareExchange(
            ref this.startupTermination,
            (int)StartupTermination.TimedOut,
            (int)StartupTermination.None) == (int)StartupTermination.None)
        {
            this.deadline.Cancel();
        }
    }
}
