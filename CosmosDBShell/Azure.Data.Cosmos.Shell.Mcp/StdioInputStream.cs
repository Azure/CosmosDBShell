// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using Microsoft.Extensions.Hosting;

internal sealed class StdioInputStream(Stream input) : Stream
{
    public IHostApplicationLifetime? HostLifetime { get; set; }

    public override bool CanRead => input.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = input.Read(buffer, offset, count);
        this.ObserveRead(read, count);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = input.Read(buffer);
        this.ObserveRead(read, buffer.Length);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await input.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        this.ObserveRead(read, count);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        this.ObserveRead(read, buffer.Length);
        return read;
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            input.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ObserveRead(int read, int requested)
    {
        if (read == 0 && requested > 0)
        {
            // RunAsync can await open subscription handlers after EOF, so shutdown cannot wait for RunAsync.
            var lifetime = this.HostLifetime ?? throw new InvalidOperationException("The stdio host lifetime has not been configured.");
            lifetime.StopApplication();
        }
    }
}
