// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class McpLocationSubscriptionTests
{
    [Fact]
    public async Task SubscribedClient_ReceivesInteractiveLocationChange()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var port = GetFreePort();

        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = port });
        await host.StartAsync(timeout.Token);
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"http://127.0.0.1:{port}/"),
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
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
    public async Task ClosedNotificationStream_RemovesSubscriptionWhileSessionStaysOpen()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var port = GetFreePort();

        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = port });
        await host.StartAsync(timeout.Token);
        try
        {
            var subscriptions = host.Services.GetRequiredService<LocationResourceSubscriptions>();
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

            using var initialize = await PostAsync(
                http,
                sessionId: null,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
                timeout.Token);
            var sessionId = Assert.Single(initialize.Headers.GetValues("Mcp-Session-Id"));
            using (await PostAsync(http, sessionId, """{"jsonrpc":"2.0","method":"notifications/initialized"}""", timeout.Token))
            {
            }

            using var streamRequest = new HttpRequestMessage(HttpMethod.Get, string.Empty);
            streamRequest.Headers.Add("Accept", "text/event-stream");
            streamRequest.Headers.Add("Mcp-Session-Id", sessionId);
            var notificationStream = await http.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            Assert.True(notificationStream.IsSuccessStatusCode);

            using (var subscribe = await PostAsync(
                http,
                sessionId,
                $$$"""{"jsonrpc":"2.0","id":2,"method":"resources/subscribe","params":{"uri":"{{{ResourceOperations.CurrentLocationUri}}}"}}""",
                timeout.Token))
            {
                Assert.True(subscribe.IsSuccessStatusCode);
            }

            Assert.Equal(1, subscriptions.SubscriberCount);

            notificationStream.Dispose();
            while (subscriptions.SubscriberCount != 0)
            {
                await Task.Delay(50, timeout.Token);
            }

            using var ping = await PostAsync(http, sessionId, """{"jsonrpc":"2.0","id":3,"method":"ping"}""", timeout.Token);
            Assert.True(ping.IsSuccessStatusCode);
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient http, string? sessionId, string json, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        if (sessionId != null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        var response = await http.SendAsync(request, cancellationToken);
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        return response;
    }
}
