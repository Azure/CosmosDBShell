// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using Azure.Data.Cosmos.Shell.Mcp;
using SdkMcpServer = ModelContextProtocol.Server.McpServer;

public class ResourceSubscriptionManagerTests
{
    [Theory]
    [InlineData("cosmos://shell/connection")]
    [InlineData("cosmos://shell/location")]
    [InlineData("cosmos://databases")]
    [InlineData("cosmos://current/containers")]
    [InlineData("cosmos://current/container/indexing-policy")]
    public void IsSubscribable_ReturnsTrue_ForStateScopedUris(string uri)
    {
        Assert.True(ResourceSubscriptionManager.IsSubscribable(uri));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cosmos://docs/commands")]
    [InlineData("cosmos://current/account")]
    [InlineData("cosmos://shell/history")]
    [InlineData("not-a-uri")]
    public void IsSubscribable_ReturnsFalse_ForUnsupportedUris(string? uri)
    {
        Assert.False(ResourceSubscriptionManager.IsSubscribable(uri));
    }

    [Fact]
    public void Subscribe_RejectsNonSubscribableUri()
    {
        var server = NewServer();
        using var manager = new ResourceSubscriptionManager();

        Assert.False(manager.Subscribe("cosmos://docs/commands", server));
        Assert.Equal(0, manager.SubscriberCount("cosmos://docs/commands"));
    }

    [Fact]
    public void Subscribe_TracksSubscriber_AndUnsubscribeRemovesIt()
    {
        var server = NewServer();
        using var manager = new ResourceSubscriptionManager();
        const string uri = "cosmos://databases";

        Assert.True(manager.Subscribe(uri, server));
        Assert.Equal(1, manager.SubscriberCount(uri));

        manager.Unsubscribe(uri, server);
        Assert.Equal(0, manager.SubscriberCount(uri));
    }

    [Fact]
    public void Subscribe_IsIdempotent_ForSameServerAndUri()
    {
        var server = NewServer();
        using var manager = new ResourceSubscriptionManager();
        const string uri = "cosmos://shell/location";

        Assert.True(manager.Subscribe(uri, server));
        Assert.True(manager.Subscribe(uri, server));
        Assert.Equal(1, manager.SubscriberCount(uri));
    }

    [Fact]
    public void Subscribe_StaysWithinPerServerCap_ForAllStateScopedUris()
    {
        var server = NewServer();
        using var manager = new ResourceSubscriptionManager();

        // A single server may subscribe to every state-scoped URI; each is well
        // under MaxSubscriptionsPerServer, so every Subscribe call succeeds.
        string[] uris =
        {
            "cosmos://shell/connection",
            "cosmos://shell/location",
            "cosmos://databases",
            "cosmos://current/containers",
            "cosmos://current/container/indexing-policy",
        };

        foreach (var uri in uris)
        {
            Assert.True(manager.Subscribe(uri, server));
        }

        Assert.True(uris.Length <= ResourceSubscriptionManager.MaxSubscriptionsPerServer);
    }

    [Fact]
    public async Task NotifyResourceUpdatedAsync_KeepsSubscriber_WhenSendSucceeds()
    {
        var server = NewServer();
        using var manager = new ResourceSubscriptionManager(
            logger: null,
            sendNotificationAsync: (_, _, _) => Task.CompletedTask);
        const string uri = "cosmos://shell/connection";

        Assert.True(manager.Subscribe(uri, server));
        await manager.NotifyResourceUpdatedAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(1, manager.SubscriberCount(uri));
    }

    [Fact]
    public async Task NotifyResourceUpdatedAsync_RemovesSubscriber_WhenSendThrows()
    {
        var server = NewServer();
        using var manager = new ResourceSubscriptionManager(
            logger: null,
            sendNotificationAsync: (_, _, _) => throw new InvalidOperationException("transport closed"));
        const string uri = "cosmos://current/containers";

        Assert.True(manager.Subscribe(uri, server));
        Assert.Equal(1, manager.SubscriberCount(uri));

        await manager.NotifyResourceUpdatedAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(0, manager.SubscriberCount(uri));
    }

    private static SdkMcpServer NewServer()
    {
        return new FakeMcpServer();
    }

    // McpServer is abstract; this minimal double is only ever used as an identity key
    // inside the manager (the notification dispatch is overridden in tests), so every
    // member simply throws.
#pragma warning disable MCPEXP002 // McpServer base constructor is experimental; safe for a test-only identity stub.
    private sealed class FakeMcpServer : SdkMcpServer
    {
        public override ModelContextProtocol.Protocol.Implementation? ClientInfo => throw new NotSupportedException();

        public override ModelContextProtocol.Protocol.ClientCapabilities? ClientCapabilities => throw new NotSupportedException();

        public override ModelContextProtocol.Server.McpServerOptions ServerOptions => throw new NotSupportedException();

        public override IServiceProvider? Services => throw new NotSupportedException();

        public override ModelContextProtocol.Protocol.LoggingLevel? LoggingLevel => throw new NotSupportedException();

        public override string? SessionId => throw new NotSupportedException();

        public override string? NegotiatedProtocolVersion => throw new NotSupportedException();

        public override Task RunAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public override IAsyncDisposable RegisterNotificationHandler(string method, Func<ModelContextProtocol.Protocol.JsonRpcNotification, CancellationToken, ValueTask> handler) => throw new NotSupportedException();

        public override Task<ModelContextProtocol.Protocol.JsonRpcResponse> SendRequestAsync(ModelContextProtocol.Protocol.JsonRpcRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public override Task SendMessageAsync(ModelContextProtocol.Protocol.JsonRpcMessage message, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public override ValueTask DisposeAsync() => throw new NotSupportedException();
    }
#pragma warning restore MCPEXP002
}
