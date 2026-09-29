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
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

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
            try
            {
                ShellInterpreter.Instance.State = new DatabaseState("McpNotificationTest", null!);
                Assert.Equal(ResourceOperations.CurrentLocationUri, await updated.Task.WaitAsync(timeout.Token));

                var resource = await client.ReadResourceAsync(ResourceOperations.CurrentLocationUri, cancellationToken: timeout.Token);
                var content = Assert.Single(resource.Contents);
                using var json = JsonDocument.Parse(Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(content).Text);
                Assert.Equal("/McpNotificationTest", json.RootElement.GetProperty("currentLocation").GetString());
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
}
