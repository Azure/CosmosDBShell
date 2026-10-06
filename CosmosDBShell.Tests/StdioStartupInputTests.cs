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

    private sealed class FaultingInput(Exception error) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(error);
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
