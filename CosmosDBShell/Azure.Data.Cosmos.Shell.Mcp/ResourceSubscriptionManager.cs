// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using McpSdkServer = ModelContextProtocol.Server.McpServer;

/// <summary>
/// Tracks which connected MCP clients have subscribed to which resource URIs
/// and broadcasts <c>notifications/resources/updated</c> when the underlying
/// shell state changes. Acts as a bridge between
/// <see cref="ShellInterpreter.StateChanged"/> and per-client MCP server instances.
/// </summary>
/// <remarks>
/// Subscriptions are best-effort: if a send fails (e.g., the client transport
/// has closed), the entry is removed silently so the manager self-heals
/// without requiring an explicit disconnect notification from the SDK.
/// </remarks>
internal sealed class ResourceSubscriptionManager : IDisposable
{
    private static readonly string[] StateScopedUris =
    {
        "cosmos://shell/connection",
        "cosmos://shell/location",
        "cosmos://databases",
        "cosmos://current/containers",
        "cosmos://current/container/indexing-policy",
    };

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<McpSdkServer, byte>> subscriptions = new(StringComparer.Ordinal);
    private readonly ILogger<ResourceSubscriptionManager>? logger;
    private readonly EventHandler<StateChangedEventArgs> stateChangedHandler;
    private bool disposed;

    public ResourceSubscriptionManager(ILogger<ResourceSubscriptionManager>? logger = null)
    {
        this.logger = logger;
        this.stateChangedHandler = this.OnShellStateChanged;
        ShellInterpreter.Instance.StateChanged += this.stateChangedHandler;
    }

    public void Subscribe(string uri, McpSdkServer server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(server);

        var set = this.subscriptions.GetOrAdd(uri, _ => new ConcurrentDictionary<McpSdkServer, byte>());
        set[server] = 0;
        this.logger?.LogInformation("MCP resource subscribed: {Uri}", uri);
    }

    public void Unsubscribe(string uri, McpSdkServer server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(server);

        if (this.subscriptions.TryGetValue(uri, out var set))
        {
            set.TryRemove(server, out _);
        }

        this.logger?.LogInformation("MCP resource unsubscribed: {Uri}", uri);
    }

    public int SubscriberCount(string uri)
    {
        return this.subscriptions.TryGetValue(uri, out var set) ? set.Count : 0;
    }

    public async Task NotifyResourceUpdatedAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!this.subscriptions.TryGetValue(uri, out var set) || set.IsEmpty)
        {
            return;
        }

        var parameters = new ResourceUpdatedNotificationParams { Uri = uri };
        foreach (var server in set.Keys)
        {
            try
            {
                await server.SendNotificationAsync(
                    NotificationMethods.ResourceUpdatedNotification,
                    parameters,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger?.LogDebug(ex, "Failed to notify subscriber for {Uri}; removing subscription.", uri);
                set.TryRemove(server, out _);
            }
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        ShellInterpreter.Instance.StateChanged -= this.stateChangedHandler;
        this.subscriptions.Clear();
    }

    private void OnShellStateChanged(object? sender, StateChangedEventArgs e)
    {
        // Fire-and-forget: notify all state-scoped URIs in the background.
        // The setter is synchronous, so we must not block it on async sends.
        foreach (var uri in StateScopedUris)
        {
            _ = this.NotifyResourceUpdatedAsync(uri);
        }
    }
}
