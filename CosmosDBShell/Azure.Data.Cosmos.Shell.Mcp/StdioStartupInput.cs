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
    private Task pump = Task.CompletedTask;

    private Exception? inputError;
    private int disposed;

    public StdioStartupInput(Stream input, TimeSpan? timeout = null)
    {
        this.input = input;
        this.Input = this.buffer.Reader.AsStream();
        this.startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            this.inputEnded.Token, this.deadline.Token);
        this.StartupToken = this.startupCancellation.Token;
        this.deadline.CancelAfter(timeout ?? StartupTimeout);
    }

    public Stream Input { get; }

    public CancellationToken StartupToken { get; }

    public Exception? InputError => Volatile.Read(ref this.inputError);

    public bool TimedOut => this.deadline.IsCancellationRequested && !this.inputEnded.IsCancellationRequested;

    public void Start() => this.pump = this.PumpAsync();

    public void CompleteStartup() => this.deadline.CancelAfter(Timeout.InfiniteTimeSpan);

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
        }
        finally
        {
            await this.buffer.Writer.CompleteAsync(error).ConfigureAwait(false);
            if (!this.stopping.IsCancellationRequested)
            {
                await this.inputEnded.CancelAsync().ConfigureAwait(false);
            }
        }
    }
}
