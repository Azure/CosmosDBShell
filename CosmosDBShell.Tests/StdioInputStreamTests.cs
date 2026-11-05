// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using Azure.Data.Cosmos.Shell.Mcp;
using Microsoft.Extensions.Hosting;
using NSubstitute;

public class StdioInputStreamTests
{
    [Fact]
    public async Task Reads_StopHostOnlyAtActualEof()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var input = new StdioInputStream(new MemoryStream([42])) { HostLifetime = lifetime };
        var buffer = new byte[1];
        Assert.Equal(0, await input.ReadAsync(Memory<byte>.Empty, TestContext.Current.CancellationToken));
        lifetime.DidNotReceive().StopApplication();
        Assert.Equal(1, await input.ReadAsync(buffer.AsMemory(), TestContext.Current.CancellationToken));
        Assert.Equal(42, buffer[0]);
        lifetime.DidNotReceive().StopApplication();
        Assert.Equal(0, await input.ReadAsync(buffer.AsMemory(), TestContext.Current.CancellationToken));
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public void SynchronousEof_StopsHost()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        using var input = new StdioInputStream(new MemoryStream()) { HostLifetime = lifetime };
        Assert.Equal(0, input.Read(new byte[1], 0, 1));
        lifetime.Received(1).StopApplication();
    }
}
