// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using Azure.Data.Cosmos.Shell.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

public class LegacyRequestLimiterTests
{
    [Fact]
    public async Task SaturatedLimiter_AllowsRepliesAndNotifications()
    {
        using var limiter = new LegacyRequestLimiter();
        var server = Substitute.For<ModelContextProtocol.Server.McpServer>();
        var releaseRequests = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controlMessages = 0;
        var handler = limiter.Limit(async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest)
            {
                await releaseRequests.Task.WaitAsync(cancellationToken);
            }
            else
            {
                controlMessages++;
            }
        });
        var requests = new List<Task>();
        try
        {
            for (var i = 0; i < LegacyRequestLimiter.MaxOutstandingRequests; i++)
            {
                requests.Add(handler(
                    new MessageContext(server, new JsonRpcRequest { Id = new RequestId(i), Method = RequestMethods.ToolsCall }),
                    TestContext.Current.CancellationToken));
            }

            Assert.Equal(LegacyRequestLimiter.MaxOutstandingRequests, limiter.OutstandingRequests);
            var error = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await handler(
                    new MessageContext(server, new JsonRpcRequest { Id = new RequestId(100), Method = RequestMethods.ToolsCall }),
                    TestContext.Current.CancellationToken));
            Assert.Equal(LegacyRequestLimiter.OverloadErrorCode, error.ErrorCode);

            await handler(
                new MessageContext(server, new JsonRpcResponse { Id = new RequestId(200), Result = null }),
                TestContext.Current.CancellationToken);
            await handler(
                new MessageContext(server, new JsonRpcNotification { Method = NotificationMethods.CancelledNotification }),
                TestContext.Current.CancellationToken);
            Assert.Equal(2, controlMessages);
            Assert.Equal(LegacyRequestLimiter.MaxOutstandingRequests, limiter.OutstandingRequests);
        }
        finally
        {
            releaseRequests.TrySetResult(true);
            await Task.WhenAll(requests);
        }

        Assert.Equal(0, limiter.OutstandingRequests);
    }
}
