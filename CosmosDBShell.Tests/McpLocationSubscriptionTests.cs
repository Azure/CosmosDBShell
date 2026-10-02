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
    public void HttpTransport_UsesStatefulSessionsWithBoundedIdleTimeout()
    {
        var options = new ModelContextProtocol.AspNetCore.HttpServerTransportOptions();
        McpServer.ConfigureHttpTransport(options);

        Assert.Equal(ModelContextProtocol.AspNetCore.HttpServerSessionMode.Stateful, options.SessionMode);
#pragma warning disable MCP9006, MCPEXP002
        Assert.Equal(McpServer.SessionIdleTimeout, options.IdleTimeout);
        Assert.NotNull(options.RunSessionHandler);
#pragma warning restore MCP9006, MCPEXP002
    }

    private static async Task<McpClient> ConnectAsync(IHost host, CancellationToken cancellationToken)
    {
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(address.TrimEnd('/') + "/"),
        });
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }
}
