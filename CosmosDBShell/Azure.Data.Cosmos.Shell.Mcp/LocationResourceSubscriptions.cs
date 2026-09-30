// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.Threading.Channels;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

internal sealed class LocationResourceSubscriptions : BackgroundService
{
    private readonly object sync = new();

    private readonly List<WeakReference<ModelContextProtocol.Server.McpServer>> subscribers = [];

    // Notifications carry only the URI, so pending changes are coalesced into one.
    private readonly Channel<bool> changes = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly ILogger<LocationResourceSubscriptions> logger;

    public LocationResourceSubscriptions(ILogger<LocationResourceSubscriptions> logger)
    {
        this.logger = logger;
        ShellInterpreter.Instance.LocationChanged += this.OnLocationChanged;
    }

    internal int SubscriberCount
    {
        get
        {
            lock (this.sync)
            {
                this.PruneSubscribers();
                return this.subscribers.Count;
            }
        }
    }

    public void Subscribe(ModelContextProtocol.Server.McpServer server, string uri)
    {
        ValidateUri(uri);
        lock (this.sync)
        {
            this.PruneSubscribers();
            if (!this.subscribers.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, server)))
            {
                this.subscribers.Add(new WeakReference<ModelContextProtocol.Server.McpServer>(server));
            }
        }
    }

    public void Unsubscribe(ModelContextProtocol.Server.McpServer server, string uri)
    {
        ValidateUri(uri);
        lock (this.sync)
        {
            this.subscribers.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, server));
        }
    }

    public void RemoveSession(string sessionId)
    {
        lock (this.sync)
        {
            this.subscribers.RemoveAll(reference =>
                !reference.TryGetTarget(out var target) || string.Equals(target.SessionId, sessionId, StringComparison.Ordinal));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var change in this.changes.Reader.ReadAllAsync(stoppingToken))
        {
            ModelContextProtocol.Server.McpServer[] servers;
            lock (this.sync)
            {
                this.PruneSubscribers();
                servers = this.subscribers
                    .Select(reference => reference.TryGetTarget(out var server) ? server : null)
                    .OfType<ModelContextProtocol.Server.McpServer>()
                    .ToArray();
            }

            foreach (var server in servers)
            {
                try
                {
                    await server.SendNotificationAsync(
                        NotificationMethods.ResourceUpdatedNotification,
                        new ResourceUpdatedNotificationParams { Uri = ResourceOperations.CurrentLocationUri },
                        cancellationToken: stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    this.logger.LogWarning(ex, "Could not notify an MCP client about the shell location change.");
                    lock (this.sync)
                    {
                        this.subscribers.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, server));
                    }
                }
            }
        }
    }

    public override void Dispose()
    {
        ShellInterpreter.Instance.LocationChanged -= this.OnLocationChanged;
        base.Dispose();
    }

    internal static void ValidateUri(string uri)
    {
        if (!string.Equals(uri, ResourceOperations.CurrentLocationUri, StringComparison.Ordinal))
        {
            throw new McpProtocolException(
                $"Resource '{uri}' does not support subscriptions. Only '{ResourceOperations.CurrentLocationUri}' can be subscribed to.",
                McpErrorCode.InvalidParams);
        }
    }

    private void OnLocationChanged()
    {
        this.changes.Writer.TryWrite(true);
    }

    private void PruneSubscribers()
    {
        this.subscribers.RemoveAll(reference => !reference.TryGetTarget(out _));
    }
}
