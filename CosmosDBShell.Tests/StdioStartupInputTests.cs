// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.IO.Pipelines;
using System.Diagnostics;
using System.Text;
using Azure.Data.Cosmos.Shell.Mcp;

public class StdioStartupInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_DeletesStartupSpoolWithoutWaitingForUninterruptibleInput(bool timedOut)
    {
        using var source = new UninterruptibleInput();
        using var input = new StdioStartupInput(
            source, timedOut ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(60));
        input.Start();
        try
        {
            await source.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (timedOut)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    Task.Delay(Timeout.InfiniteTimeSpan, input.StartupToken)
                        .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.True(input.TimedOut);
            }

            input.Dispose();

            Assert.False(source.Release.Task.IsCompleted);
            Assert.False(File.Exists(input.StartupBufferPath));
        }
        finally
        {
            source.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task CompletedStartup_ReusesSpoolHandleAndDeletesItAfterReading()
    {
        using var source = new UninterruptibleInput();
        using var input = new StdioStartupInput(source);
        input.Start();
        try
        {
            await source.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await input.CompleteStartupAsync();
            source.Release.TrySetResult();
            using var received = new MemoryStream();
            await input.Input.CopyToAsync(received, TestContext.Current.CancellationToken);

            Assert.Equal(UninterruptibleInput.Prefix, received.ToArray());
            Assert.False(File.Exists(input.StartupBufferPath));
        }
        finally
        {
            source.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task Eof_CancelsStartupAndPreservesBufferedProtocolBytes()
    {
        var source = new Pipe();
        using var input = new StdioStartupInput(source.Reader.AsStream());
        input.Start();
        var bytes = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}\n");
        await source.Writer.WriteAsync(bytes, TestContext.Current.CancellationToken);
        await source.Writer.CompleteAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(Timeout.InfiniteTimeSpan, input.StartupToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(input.TimedOut);
        Assert.Null(input.InputError);
        Assert.Empty(ReportCancellation(input).Messages);
        using var received = new MemoryStream();
        await input.Input.CopyToAsync(received, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, received.ToArray());
    }

    [Fact]
    public async Task PendingInput_StartupDeadlineCancelsWithoutEof()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), StdioStartupInput.StartupTimeout);
        var source = new Pipe();
        using var input = new StdioStartupInput(source.Reader.AsStream(), TimeSpan.FromMilliseconds(100));
        input.Start();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(Timeout.InfiniteTimeSpan, input.StartupToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(input.TimedOut);
        Assert.Null(input.InputError);
        await source.Writer.CompleteAsync();
    }

    [Fact]
    public async Task CompletedStartup_DisablesDeadlineAndContinuesReading()
    {
        var source = new Pipe();
        using var input = new StdioStartupInput(source.Reader.AsStream(), TimeSpan.FromMilliseconds(100));
        input.Start();
        await input.CompleteStartupAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        Assert.False(input.StartupToken.IsCancellationRequested);
        Assert.False(input.TimedOut);

        await source.Writer.WriteAsync(new byte[] { 42 }, TestContext.Current.CancellationToken);
        var received = new byte[1];
        Assert.Equal(1, await input.Input.ReadAsync(received.AsMemory(), TestContext.Current.CancellationToken));
        Assert.Equal(42, received[0]);
        await source.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LargeQueuedInput_DrainsThroughEofAndPreservesBytes()
    {
        var source = new Pipe();
        using var input = new StdioStartupInput(source.Reader.AsStream());
        input.Start();
        var bytes = new byte[256 * 1024];
        Random.Shared.NextBytes(bytes);

        await source.Writer.WriteAsync(bytes, TestContext.Current.CancellationToken);
        await source.Writer.CompleteAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(Timeout.InfiniteTimeSpan, input.StartupToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        using var received = new MemoryStream();
        await input.Input.CopyToAsync(received, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, received.ToArray());
        Assert.False(input.TimedOut);
    }

    [Fact]
    public async Task ProductionDeadline_IsSixtySecondsAndTimeoutReportsConnectionError()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), StdioStartupInput.StartupTimeout);
        var source = new Pipe();
        using var input = new StdioStartupInput(source.Reader.AsStream(), TimeSpan.FromMilliseconds(50));
        input.Start();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(Timeout.InfiniteTimeSpan, input.StartupToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        await source.Writer.CompleteAsync();
        var received = new byte[1];
        Assert.Equal(0, await input.Input.ReadAsync(received, TestContext.Current.CancellationToken));
        Assert.True(input.TimedOut);
        var (exitCode, messages) = ReportCancellation(input);
        Assert.Equal(4, exitCode);
        Assert.Contains("timed out after 60 seconds", Assert.Single(messages));
    }

    [Fact]
    public async Task InputFailure_CancelsStartupAndSurfacesOriginalError()
    {
        var error = new IOException("STDIO_INPUT_FAILURE");
        using var input = new StdioStartupInput(new FaultingInput(error));
        input.Start();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(Timeout.InfiniteTimeSpan, input.StartupToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(error, input.InputError);
        Assert.False(input.TimedOut);
        var (exitCode, messages) = ReportCancellation(input);
        Assert.Equal(1, exitCode);
        Assert.Contains("STDIO_INPUT_FAILURE", Assert.Single(messages));
        var received = new byte[1];
        var actual = await Assert.ThrowsAsync<IOException>(() =>
            input.Input.ReadAsync(received.AsMemory(), TestContext.Current.CancellationToken).AsTask());
        Assert.Same(error, actual);
    }

    [Fact]
    public async Task StartupBuffer_ExactlyAtLimit_PreservesAllBytesWithoutCancellation()
    {
        Assert.Equal(8 * 1024 * 1024, StdioStartupInput.MaximumStartupBufferBytes);
        var bytes = new byte[StdioStartupInput.MaximumStartupBufferBytes];
        Random.Shared.NextBytes(bytes);
        using var source = new UninterruptibleInput(bytes);
        using var input = new StdioStartupInput(source);
        input.Start();
        try
        {
            await source.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(input.StartupToken.IsCancellationRequested);
            Assert.Null(input.InputError);
            await input.CompleteStartupAsync();
            source.Release.TrySetResult();
            using var received = new MemoryStream();
            await input.Input.CopyToAsync(received, TestContext.Current.CancellationToken);

            Assert.Equal(bytes, received.ToArray());
            Assert.False(File.Exists(input.StartupBufferPath));
        }
        finally
        {
            source.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task StartupBuffer_OneByteOverLimit_CancelsReportsErrorAndNeverExceedsLimit()
    {
        using var source = new MemoryStream(new byte[StdioStartupInput.MaximumStartupBufferBytes + 1]);
        using var input = new StdioStartupInput(source);
        input.Start();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Delay(Timeout.InfiniteTimeSpan, input.StartupToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.False(input.TimedOut);
        var error = Assert.IsType<IOException>(input.InputError);
        var (exitCode, messages) = ReportCancellation(input);
        Assert.Equal(1, exitCode);
        Assert.Contains("8 MiB", Assert.Single(messages));
        using var received = new MemoryStream();
        var actual = await Assert.ThrowsAsync<IOException>(() =>
            input.Input.CopyToAsync(received, TestContext.Current.CancellationToken));
        Assert.Same(error, actual);
        Assert.Equal(StdioStartupInput.MaximumStartupBufferBytes, received.Length);
        Assert.False(File.Exists(input.StartupBufferPath));
    }

    [Fact]
    public async Task CompletedStartup_InputOverStartupLimit_IsNotRejected()
    {
        var bytes = new byte[StdioStartupInput.MaximumStartupBufferBytes + 1];
        Random.Shared.NextBytes(bytes);
        using var source = new MemoryStream(bytes);
        using var input = new StdioStartupInput(source);
        await input.CompleteStartupAsync();
        input.Start();
        using var received = new MemoryStream();
        await input.Input.CopyToAsync(received, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, received.ToArray());
        Assert.Null(input.InputError);
        Assert.False(input.TimedOut);
    }

    private sealed class FaultingInput(Exception error) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(error);
    }

    private sealed class UninterruptibleInput(byte[]? prefix = null) : MemoryStream(prefix ?? Prefix)
    {
        internal static readonly byte[] Prefix = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"method\":\"ping\"}\n");

        internal TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (this.Position < this.Length)
            {
                return await base.ReadAsync(buffer, cancellationToken);
            }

            this.Blocked.TrySetResult();
            await this.Release.Task;
            return 0;
        }
    }

    private static (int ExitCode, List<string> Messages) ReportCancellation(StdioStartupInput input)
    {
        var originalExitCode = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            var messages = new List<string>();
            Program.ReportStdioStartupCancellation(input, messages.Add);
            return (Environment.ExitCode, messages);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
        }
    }
}
