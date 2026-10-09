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
    [Theory]
    [InlineData(HttpTransportMode.StreamableHttp, "2025-11-25")]
    [InlineData(HttpTransportMode.Sse, "2024-11-05")]
    public async Task SubscribedClient_ReceivesInteractiveLocationChange(HttpTransportMode transportMode, string protocolVersion)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            await using var client = await ConnectAsync(host, timeout.Token, protocolVersion, transportMode);
            Assert.NotNull(client.ServerCapabilities.Resources);
            Assert.True(client.ServerCapabilities.Resources.Subscribe);
            Assert.False(client.ServerCapabilities.Resources.ListChanged);
            var resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
            Assert.Equal(3, resources.Count);
            Assert.Contains(resources, resource => resource.Uri == ResourceOperations.CurrentLocationUri);
            foreach (var documentation in resources.Where(resource => resource.Uri.StartsWith("cosmos://docs/", StringComparison.Ordinal)))
            {
                var result = await client.ReadResourceAsync(documentation.Uri, cancellationToken: timeout.Token);
                Assert.NotEmpty(Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text);
            }

            Assert.Empty(await client.ListResourceTemplatesAsync(cancellationToken: timeout.Token));

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

    [Theory]
    [InlineData(HttpTransportMode.StreamableHttp, "2025-11-25")]
    [InlineData(HttpTransportMode.Sse, "2024-11-05")]
    public async Task EndedSession_RemovesLocationSubscription(HttpTransportMode transportMode, string protocolVersion)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            var subscriptions = host.Services.GetRequiredService<LocationResourceSubscriptions>();
            await using var client = await ConnectAsync(host, timeout.Token, protocolVersion, transportMode);
            await client.SubscribeToResourceAsync(ResourceOperations.CurrentLocationUri, cancellationToken: timeout.Token);
            Assert.Equal(1, subscriptions.SubscriberCount);

            // Closing either transport must release its session's subscription.
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

    [Theory]
    [InlineData("2025-11-25", McpErrorCode.ResourceNotFound)]
    [InlineData(null, McpErrorCode.InvalidParams)]
    public async Task UnknownResource_PreservesProtocolError(string? protocolVersion, McpErrorCode errorCode)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            await using var client = await ConnectAsync(host, timeout.Token, protocolVersion);
            var error = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await client.ReadResourceAsync("cosmos://docs/missing", cancellationToken: timeout.Token));
            Assert.Equal(errorCode, error.ErrorCode);
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
            Assert.NotNull(client.ServerCapabilities.Resources);
            Assert.True(client.ServerCapabilities.Resources.Subscribe);
            Assert.False(client.ServerCapabilities.Resources.ListChanged);

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
                            ResourcesListChanged = true,
                        },
                    }),
                },
                listenCancellation.Token);

            // Only the current-location resource is honored.
            var acknowledgement = await acknowledged.Task.WaitAsync(timeout.Token);
            var granted = acknowledgement.Params!["notifications"]!.AsObject();
            Assert.Equal(ResourceOperations.CurrentLocationUri, Assert.Single(granted["resourceSubscriptions"]!.AsArray())!.GetValue<string>());
            Assert.False(granted.ContainsKey("toolsListChanged"));
            Assert.False(granted.ContainsKey("resourcesListChanged"));
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
                            ResourcesListChanged = true,
                        },
                    }),
                },
                timeout.Token);

            Assert.Equal("unsupported-listen", response.Id.ToString());
            var acknowledgement = await acknowledged.Task.WaitAsync(timeout.Token);
            var granted = acknowledgement.Params!["notifications"]!.AsObject();
            Assert.False(granted.ContainsKey("resourceSubscriptions"));
            Assert.False(granted.ContainsKey("toolsListChanged"));
            Assert.False(granted.ContainsKey("resourcesListChanged"));
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
    public async Task LegacyAndStreamableHttpClients_ShareServerToolsAndResources()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            await using var modern = await ConnectAsync(host, timeout.Token);
            await using var legacy = await ConnectAsync(host, timeout.Token, "2024-11-05", HttpTransportMode.Sse);
            var modernTools = await modern.ListToolsAsync(cancellationToken: timeout.Token);
            var legacyTools = await legacy.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.NotEmpty(modernTools);
            Assert.Equal(modernTools.Select(tool => tool.Name), legacyTools.Select(tool => tool.Name));

            var modernLocation = await modern.ReadResourceAsync(ResourceOperations.CurrentLocationUri, cancellationToken: timeout.Token);
            var legacyLocation = await legacy.ReadResourceAsync(ResourceOperations.CurrentLocationUri, cancellationToken: timeout.Token);
            Assert.Equal(
                Assert.IsType<TextResourceContents>(Assert.Single(modernLocation.Contents)).Text,
                Assert.IsType<TextResourceContents>(Assert.Single(legacyLocation.Contents)).Text);

            foreach (var client in new[] { modern, legacy })
            {
                var result = await client.CallToolAsync("version", cancellationToken: timeout.Token);
                Assert.False(result.IsError == true);
                Assert.NotEmpty(result.Content);
            }
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("/sse", "GET")]
    [InlineData("/message", "POST")]
    public async Task LegacyEndpoints_RejectNonLoopbackOrigins(string path, string method)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(new HttpMethod(method), address.TrimEnd('/') + path);
            request.Headers.Add("Origin", "https://untrusted.example");
            using var response = await http.SendAsync(request, timeout.Token);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void HttpTransport_UsesSessionsOnlyForInitializeClientsWithBoundedIdleTimeout()
    {
        var options = new ModelContextProtocol.AspNetCore.HttpServerTransportOptions();
        McpServer.ConfigureHttpTransport(options);

        Assert.Equal(ModelContextProtocol.AspNetCore.HttpServerSessionMode.StatefulForInitializeClients, options.SessionMode);
#pragma warning disable MCP9004 // Verify the intentionally enabled legacy transport.
        Assert.True(options.EnableLegacySse);
#pragma warning restore MCP9004
#pragma warning disable MCP9006, MCPEXP002
        Assert.Equal(McpServer.SessionIdleTimeout, options.IdleTimeout);
        Assert.NotNull(options.RunSessionHandler);
#pragma warning restore MCP9006, MCPEXP002
    }

    // resources/subscribe and session lifetimes exist only for clients that use the initialize handshake.
    private static async Task<McpClient> ConnectAsync(
        IHost host,
        CancellationToken cancellationToken,
        string? protocolVersion = "2025-11-25",
        HttpTransportMode transportMode = HttpTransportMode.StreamableHttp)
    {
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(address.TrimEnd('/') + (transportMode == HttpTransportMode.Sse ? "/sse" : "/")),
            TransportMode = transportMode,
        });
        return await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion }, cancellationToken: cancellationToken);
    }
}
