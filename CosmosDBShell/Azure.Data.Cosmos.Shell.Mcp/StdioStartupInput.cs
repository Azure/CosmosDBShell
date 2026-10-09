// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.IO.Pipelines;

internal sealed class StdioStartupInput : IDisposable
{
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    private readonly Stream input;
    private readonly Pipe liveBuffer = new();
    private readonly string startupBufferPath = Path.Join(Path.GetTempPath(), $"cosmos-stdio-{Guid.NewGuid():N}.tmp");
    private readonly FileStream startupBuffer;
    private readonly SemaphoreSlim startupBufferLock = new(1, 1);
    private readonly TaskCompletionSource<Stream> startupBufferReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource inputEnded = new();
    private readonly CancellationTokenSource deadline = new();
    private readonly CancellationTokenSource startupCancellation;
    private readonly Timer deadlineTimer;
    private Task pump = Task.CompletedTask;

    private Exception? inputError;
    private bool startupBufferCompleted;
    private int startupTermination;
    private int disposed;

    public StdioStartupInput(Stream input, TimeSpan? timeout = null)
    {
        this.input = input;
        var bufferOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            bufferOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        this.startupBuffer = new FileStream(this.startupBufferPath, bufferOptions);
        this.Input = new StartupBufferedStream(
            this.startupBufferReady.Task,
            this.liveBuffer.Reader.AsStream());
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

    internal string StartupBufferPath => this.startupBufferPath;

    public CancellationToken StartupToken { get; }

    public Exception? InputError => Volatile.Read(ref this.startupTermination) == (int)StartupTermination.InputError
        ? Volatile.Read(ref this.inputError)
        : null;

    public bool TimedOut => Volatile.Read(ref this.startupTermination) == (int)StartupTermination.TimedOut;

    public void Start() => this.pump = this.PumpAsync();

    public async Task CompleteStartupAsync()
    {
        this.deadlineTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        await this.CompleteStartupBufferAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        this.stopping.Cancel();

        // The stdin pump may never unblock. Release the spool independently of its lifetime.
        this.startupBufferLock.Wait();
        try
        {
            this.startupBufferCompleted = true;
            this.startupBuffer.Dispose();
            this.startupBufferReady.TrySetCanceled(this.stopping.Token);
        }
        finally
        {
            this.startupBufferLock.Release();
        }

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
                this.startupBufferLock.Dispose();
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
            var bytes = new byte[4096];
            while (true)
            {
                var read = await this.input.ReadAsync(bytes, this.stopping.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await this.startupBufferLock.WaitAsync(this.stopping.Token).ConfigureAwait(false);
                try
                {
                    if (!this.startupBufferCompleted)
                    {
                        await this.startupBuffer.WriteAsync(
                            bytes.AsMemory(0, read),
                            this.stopping.Token).ConfigureAwait(false);
                        continue;
                    }
                }
                finally
                {
                    this.startupBufferLock.Release();
                }

                await this.liveBuffer.Writer.WriteAsync(
                    bytes.AsMemory(0, read),
                    this.stopping.Token).ConfigureAwait(false);
            }
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
            await this.CompleteStartupBufferAsync().ConfigureAwait(false);
            await this.liveBuffer.Writer.CompleteAsync(error).ConfigureAwait(false);
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

    private async Task CompleteStartupBufferAsync()
    {
        await this.startupBufferLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this.startupBufferCompleted)
            {
                return;
            }

            await this.startupBuffer.FlushAsync().ConfigureAwait(false);
            this.startupBuffer.Position = 0;
            this.startupBufferCompleted = true;
            if (!this.startupBufferReady.TrySetResult(this.startupBuffer))
            {
                await this.startupBuffer.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            this.startupBufferLock.Release();
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

    private sealed class StartupBufferedStream(Task<Stream> startupBuffer, Stream liveInput) : Stream
    {
        private Stream? prefix;
        private bool prefixCompleted;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

#pragma warning disable VSTHRD002 // Synchronously waiting is required by the Stream.Read contract.
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!this.prefixCompleted)
            {
                this.prefix ??= startupBuffer.GetAwaiter().GetResult();
                var read = this.prefix.Read(buffer, offset, count);
                if (read > 0)
                {
                    return read;
                }

                this.prefixCompleted = true;
                this.prefix.Dispose();
            }

            return liveInput.Read(buffer, offset, count);
        }
#pragma warning restore VSTHRD002

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!this.prefixCompleted)
            {
                this.prefix ??= await startupBuffer.WaitAsync(cancellationToken).ConfigureAwait(false);
                var read = await this.prefix.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read > 0)
                {
                    return read;
                }

                this.prefixCompleted = true;
                await this.prefix.DisposeAsync().ConfigureAwait(false);
            }

            return await liveInput.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.prefix?.Dispose();
                liveInput.Dispose();
                if (this.prefix is null)
                {
                    _ = startupBuffer.ContinueWith(
                        static task => task.Result.Dispose(),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }

            base.Dispose(disposing);
        }
    }
}
