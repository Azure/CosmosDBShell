// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

public class LegacyHttpAdmissionTests : IDisposable
{
    private readonly List<MemoryStream> requestBodies = [];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowSseResponse_RejectsExcessHttpRequestsBeforeDispatch(bool errorResponse)
    {
        var admission = new LegacyHttpAdmission();
        var dispatched = 0;
        RequestDelegate dispatch = context =>
        {
            dispatched++;
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        };
        for (var i = 0; i < LegacyRequestLimiter.MaxOutstandingRequests; i++)
        {
            await admission.InvokeAsync(CreateContext(CreateRequest(i)), dispatch);
        }

        var server = Substitute.For<ModelContextProtocol.Server.McpServer>();
        server.SessionId.Returns("legacy-test");
        var releaseStream = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var outgoing = admission.TrackOutgoing(
            async (_, cancellationToken) => await releaseStream.Task.WaitAsync(cancellationToken));
        JsonRpcMessage response = errorResponse
            ? new JsonRpcError { Id = new RequestId(0), Error = new JsonRpcErrorDetail { Code = -32601, Message = "Unknown method." } }
            : new JsonRpcResponse { Id = new RequestId(0), Result = null };
        var pendingResponse = outgoing(new MessageContext(server, response), TestContext.Current.CancellationToken);
        try
        {
            for (var i = 0; i < 32; i++)
            {
                var excess = CreateContext(CreateRequest(100 + i));
                await admission.InvokeAsync(excess, dispatch);
                Assert.Equal(StatusCodes.Status429TooManyRequests, excess.Response.StatusCode);
                Assert.Equal("1", excess.Response.Headers.RetryAfter.ToString());
            }

            Assert.Equal(LegacyRequestLimiter.MaxOutstandingRequests, dispatched);
            Assert.Equal(LegacyRequestLimiter.MaxOutstandingRequests, admission.OutstandingRequests);
            Assert.False(pendingResponse.IsCompleted);

            var cancellation = CreateContext(new JsonRpcNotification { Method = NotificationMethods.CancelledNotification });
            await admission.InvokeAsync(cancellation, dispatch);
            var reply = CreateContext(new JsonRpcResponse { Id = new RequestId(200), Result = null });
            await admission.InvokeAsync(reply, dispatch);
            Assert.Equal(StatusCodes.Status202Accepted, cancellation.Response.StatusCode);
            Assert.Equal(StatusCodes.Status202Accepted, reply.Response.StatusCode);
            Assert.Equal(LegacyRequestLimiter.MaxOutstandingRequests, admission.OutstandingRequests);
        }
        finally
        {
            releaseStream.TrySetResult(true);
            await pendingResponse.WaitAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(LegacyRequestLimiter.MaxOutstandingRequests - 1, admission.OutstandingRequests);
        var retry = CreateContext(CreateRequest(300));
        await admission.InvokeAsync(retry, dispatch);
        Assert.Equal(StatusCodes.Status202Accepted, retry.Response.StatusCode);
        Assert.Equal(LegacyRequestLimiter.MaxOutstandingRequests, admission.OutstandingRequests);
        admission.EndSession("legacy-test");
        Assert.Equal(0, admission.OutstandingRequests);
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public async Task RejectedHttpRequest_ReleasesAdmission(int statusCode)
    {
        var admission = new LegacyHttpAdmission();
        var context = CreateContext(CreateRequest(1));
        await admission.InvokeAsync(context, current =>
        {
            current.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        });
        Assert.Equal(0, admission.OutstandingRequests);
    }

    [Fact]
    public async Task CancelledHandler_ReleasesAdmission()
    {
        var admission = new LegacyHttpAdmission();
        await admission.InvokeAsync(CreateContext(CreateRequest(1)), context =>
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        });
        var server = Substitute.For<ModelContextProtocol.Server.McpServer>();
        server.SessionId.Returns("legacy-test");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = admission.TrackIncoming((_, token) => Task.FromCanceled(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler(new MessageContext(server, CreateRequest(1)), cancellation.Token));
        Assert.Equal(0, admission.OutstandingRequests);
    }

    public void Dispose()
    {
        foreach (var body in this.requestBodies)
        {
            body.Dispose();
        }
    }

    private static JsonRpcRequest CreateRequest(int id)
    {
        return new JsonRpcRequest { Id = new RequestId(id), Method = RequestMethods.ToolsCall };
    }

    private DefaultHttpContext CreateContext(JsonRpcMessage message)
    {
        var body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(message));
        this.requestBodies.Add(body);
        var context = new DefaultHttpContext();
        context.Request.Path = "/message";
        context.Request.Method = HttpMethods.Post;
        context.Request.QueryString = new QueryString("?sessionId=legacy-test");
        context.Request.ContentType = "application/json";
        context.Request.Body = body;
        context.Response.Body = Stream.Null;
        return context;
    }
}
