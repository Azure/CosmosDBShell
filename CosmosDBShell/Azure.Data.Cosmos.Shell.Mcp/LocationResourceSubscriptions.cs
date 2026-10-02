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

    // Session servers keyed by session ID. Entries are added when a session starts running and
    // removed when it ends (DELETE, idle timeout, or shutdown), so no dead session is retained.
    private readonly Dictionary<string, ModelContextProtocol.Server.McpServer> sessions = new(StringComparer.Ordinal);

    private readonly HashSet<string> subscribedSessionIds = new(StringComparer.Ordinal);

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
                return this.subscribedSessionIds.Count;
            }
        }
    }

    /// <summary>
    /// Runs an MCP session and keeps it available for notifications until the session ends.
    /// </summary>
    public async Task RunSessionAsync(ModelContextProtocol.Server.McpServer server, CancellationToken cancellationToken)
    {
        var sessionId = server.SessionId;
        if (sessionId is not null)
        {
            lock (this.sync)
            {
                this.sessions[sessionId] = server;
            }
        }

        try
        {
            await server.RunAsync(cancellationToken);
        }
        finally
        {
            if (sessionId is not null)
            {
                lock (this.sync)
                {
                    if (this.sessions.TryGetValue(sessionId, out var current) && ReferenceEquals(current, server))
                    {
                        this.sessions.Remove(sessionId);
                        this.subscribedSessionIds.Remove(sessionId);
                    }
                }
            }
        }
    }

    public void Subscribe(string? sessionId, string uri)
    {
        ValidateUri(uri);
        if (sessionId is null)
        {
            throw new McpProtocolException(
                "Resource subscriptions require a stateful MCP session.",
                McpErrorCode.InvalidRequest);
        }

        lock (this.sync)
        {
            if (this.sessions.ContainsKey(sessionId))
            {
                this.subscribedSessionIds.Add(sessionId);
            }
        }
    }

    public void Unsubscribe(string? sessionId, string uri)
    {
        ValidateUri(uri);
        if (sessionId is null)
        {
            return;
        }

        lock (this.sync)
        {
            this.subscribedSessionIds.Remove(sessionId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var change in this.changes.Reader.ReadAllAsync(stoppingToken))
        {
            ModelContextProtocol.Server.McpServer[] servers;
            lock (this.sync)
            {
                servers = this.subscribedSessionIds
                    .Select(sessionId => this.sessions.TryGetValue(sessionId, out var server) ? server : null)
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
                    // Session cleanup is tied to the session lifetime; a failed send only affects this notification.
                    this.logger.LogWarning(ex, "Could not notify an MCP client about the shell location change.");
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
}
