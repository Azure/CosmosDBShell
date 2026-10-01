// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class McpLocationSubscriptionTests
{
    [Fact]
    public async Task SubscribedClient_ReceivesInteractiveLocationChange()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            await using var client = await ConnectAsync(host, timeout.Token);
            var resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
            Assert.Contains(resources, resource => resource.Uri == ResourceOperations.CurrentLocationUri);

            var invalid = await Assert.ThrowsAsync<McpProtocolException>(
                () => client.SubscribeToResourceAsync("cosmos://docs/scripting", cancellationToken: timeout.Token));
            Assert.Equal(McpErrorCode.InvalidParams, invalid.ErrorCode);
            Assert.Contains(ResourceOperations.CurrentLocationUri, invalid.Message);

            var updated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var subscription = await client.SubscribeToResourceAsync(
                ResourceOperations.CurrentLocationUri,
                (notification, _) =>
                {
                    updated.TrySetResult(notification.Uri);
                    return ValueTask.CompletedTask;
                },
                cancellationToken: timeout.Token);

            var originalState = ShellInterpreter.Instance.State;
            using var cosmosClient = new CosmosClient(
                "https://localhost:8081",
                Convert.ToBase64String(new byte[64]),
                new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
            try
            {
                // The subscription must not depend on per-request objects that the GC can reclaim.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                ShellInterpreter.Instance.State = new DatabaseState("McpNotificationTest", cosmosClient);
                Assert.Equal(ResourceOperations.CurrentLocationUri, await updated.Task.WaitAsync(timeout.Token));

                var resource = await client.ReadResourceAsync(ResourceOperations.CurrentLocationUri, cancellationToken: timeout.Token);
                var content = Assert.Single(resource.Contents);
                using var json = JsonDocument.Parse(Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(content).Text);
                Assert.Equal("/McpNotificationTest", json.RootElement.GetProperty("currentLocation").GetString());
                Assert.Equal(cosmosClient.Endpoint.ToString(), json.RootElement.GetProperty("currentAccountEndpoint").GetString());
            }
            finally
            {
                ShellInterpreter.Instance.State = originalState;
            }
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task EndedSession_RemovesLocationSubscription()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            var subscriptions = host.Services.GetRequiredService<LocationResourceSubscriptions>();
            var client = await ConnectAsync(host, timeout.Token);
            await client.SubscribeToResourceAsync(ResourceOperations.CurrentLocationUri, cancellationToken: timeout.Token);
            Assert.Equal(1, subscriptions.SubscriberCount);

            // Disposing the client ends the session with DELETE, which must release the subscription.
            await client.DisposeAsync();
            while (subscriptions.SubscriberCount != 0)
            {
                await Task.Delay(20, timeout.Token);
            }
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ListeningClient_ReceivesLocationChangeOnListenStream()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            var subscriptions = host.Services.GetRequiredService<LocationResourceSubscriptions>();
            await using var client = await ConnectAsync(host, timeout.Token, protocolVersion: null);
            Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);

            var acknowledged = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            var updated = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var acknowledgedHandler = client.RegisterNotificationHandler(
                NotificationMethods.SubscriptionsAcknowledgedNotification,
                (notification, _) =>
                {
                    acknowledged.TrySetResult(notification);
                    return ValueTask.CompletedTask;
                });
            await using var updatedHandler = client.RegisterNotificationHandler(
                NotificationMethods.ResourceUpdatedNotification,
                (notification, _) =>
                {
                    updated.TrySetResult(notification);
                    return ValueTask.CompletedTask;
                });

            using var listenCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var listen = client.SendRequestAsync(
                new JsonRpcRequest
                {
                    Id = new RequestId("location-listen"),
                    Method = RequestMethods.SubscriptionsListen,
                    Params = JsonSerializer.SerializeToNode(new SubscriptionsListenRequestParams
                    {
                        Notifications = new SubscriptionsListenNotifications
                        {
                            ResourceSubscriptions = [ResourceOperations.CurrentLocationUri, "cosmos://docs/scripting"],
                            ToolsListChanged = true,
                        },
                    }),
                },
                listenCancellation.Token);

            // Only the current-location resource is honored.
            var acknowledgement = await acknowledged.Task.WaitAsync(timeout.Token);
            var granted = acknowledgement.Params!["notifications"]!.AsObject();
            Assert.Equal(ResourceOperations.CurrentLocationUri, Assert.Single(granted["resourceSubscriptions"]!.AsArray())!.GetValue<string>());
            Assert.False(granted.ContainsKey("toolsListChanged"));
            Assert.Equal("location-listen", acknowledgement.Params["_meta"]![MetaKeys.SubscriptionId]!.GetValue<string>());
            while (subscriptions.ListenerCount != 1)
            {
                await Task.Delay(20, timeout.Token);
            }

            var originalState = ShellInterpreter.Instance.State;
            using var cosmosClient = new CosmosClient(
                "https://localhost:8081",
                Convert.ToBase64String(new byte[64]),
                new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
            try
            {
                ShellInterpreter.Instance.State = new DatabaseState("McpListenTest", cosmosClient);
                var notification = await updated.Task.WaitAsync(timeout.Token);
                Assert.Equal(ResourceOperations.CurrentLocationUri, notification.Params!["uri"]!.GetValue<string>());
                Assert.Equal("location-listen", notification.Params["_meta"]![MetaKeys.SubscriptionId]!.GetValue<string>());
            }
            finally
            {
                ShellInterpreter.Instance.State = originalState;
            }

            // Cancelling the listen request ends the stream and releases the listener.
            await listenCancellation.CancelAsync();
            try
            {
                await listen;
                Assert.Fail("The listen request should end only when cancelled.");
            }
            catch (OperationCanceledException)
            {
                Assert.True(listenCancellation.IsCancellationRequested);
            }

            while (subscriptions.ListenerCount != 0)
            {
                await Task.Delay(20, timeout.Token);
            }
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ListenWithOnlyUnsupportedFilters_AcknowledgesNothingAndCompletes()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            var subscriptions = host.Services.GetRequiredService<LocationResourceSubscriptions>();
            await using var client = await ConnectAsync(host, timeout.Token, protocolVersion: null);

            var acknowledged = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var acknowledgedHandler = client.RegisterNotificationHandler(
                NotificationMethods.SubscriptionsAcknowledgedNotification,
                (notification, _) =>
                {
                    acknowledged.TrySetResult(notification);
                    return ValueTask.CompletedTask;
                });

            var response = await client.SendRequestAsync(
                new JsonRpcRequest
                {
                    Id = new RequestId("unsupported-listen"),
                    Method = RequestMethods.SubscriptionsListen,
                    Params = JsonSerializer.SerializeToNode(new SubscriptionsListenRequestParams
                    {
                        Notifications = new SubscriptionsListenNotifications
                        {
                            ResourceSubscriptions = ["cosmos://docs/scripting"],
                            ToolsListChanged = true,
                        },
                    }),
                },
                timeout.Token);

            Assert.Equal("unsupported-listen", response.Id.ToString());
            var acknowledgement = await acknowledged.Task.WaitAsync(timeout.Token);
            var granted = acknowledgement.Params!["notifications"]!.AsObject();
            Assert.False(granted.ContainsKey("resourceSubscriptions"));
            Assert.False(granted.ContainsKey("toolsListChanged"));
            Assert.Equal("unsupported-listen", acknowledgement.Params["_meta"]![MetaKeys.SubscriptionId]!.GetValue<string>());
            Assert.Equal(0, subscriptions.ListenerCount);
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task HostStop_EndsOpenListenStreamWithoutWaitingForShutdownTimeout()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        var subscriptions = host.Services.GetRequiredService<LocationResourceSubscriptions>();
        await using var client = await ConnectAsync(host, timeout.Token, protocolVersion: null);

        _ = client.SendRequestAsync(
            new JsonRpcRequest
            {
                Id = new RequestId("shutdown-listen"),
                Method = RequestMethods.SubscriptionsListen,
                Params = JsonSerializer.SerializeToNode(new SubscriptionsListenRequestParams
                {
                    Notifications = new SubscriptionsListenNotifications
                    {
                        ResourceSubscriptions = [ResourceOperations.CurrentLocationUri],
                    },
                }),
            },
            timeout.Token);
        while (subscriptions.ListenerCount != 1)
        {
            await Task.Delay(20, timeout.Token);
        }

        // The web server stops first and waits for open requests until the host shutdown timeout (30 s by default).
        await host.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, subscriptions.ListenerCount);
    }

    [Fact]
    public void HttpTransport_UsesSessionsOnlyForInitializeClientsWithBoundedIdleTimeout()
    {
        var options = new ModelContextProtocol.AspNetCore.HttpServerTransportOptions();
        McpServer.ConfigureHttpTransport(options);

        Assert.Equal(ModelContextProtocol.AspNetCore.HttpServerSessionMode.StatefulForInitializeClients, options.SessionMode);
#pragma warning disable MCP9006, MCPEXP002
        Assert.Equal(McpServer.SessionIdleTimeout, options.IdleTimeout);
        Assert.NotNull(options.RunSessionHandler);
#pragma warning restore MCP9006, MCPEXP002
    }

    // resources/subscribe and session lifetimes exist only for clients that use the initialize handshake.
    private static async Task<McpClient> ConnectAsync(IHost host, CancellationToken cancellationToken, string? protocolVersion = "2025-11-25")
    {
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(address.TrimEnd('/') + "/"),
        });
        return await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: cancellationToken);
    }
}
