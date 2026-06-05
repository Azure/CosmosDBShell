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
    /// <summary>
    /// Hard cap on the number of distinct URI subscriptions a single connected
    /// MCP server (client transport) can hold at once. Bounds memory growth
    /// from a misbehaving or malicious client.
    /// </summary>
    internal const int MaxSubscriptionsPerServer = 64;

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
    private readonly Func<McpSdkServer, ResourceUpdatedNotificationParams, CancellationToken, Task> sendNotificationAsync;
    private bool disposed;

    public ResourceSubscriptionManager(ILogger<ResourceSubscriptionManager>? logger = null)
        : this(logger, null)
    {
    }

    /// <summary>
    /// Test seam constructor that allows substituting the notification dispatch so unit
    /// tests can simulate transport failures without a live MCP client connection.
    /// </summary>
    internal ResourceSubscriptionManager(
        ILogger<ResourceSubscriptionManager>? logger,
        Func<McpSdkServer, ResourceUpdatedNotificationParams, CancellationToken, Task>? sendNotificationAsync)
    {
        this.logger = logger;
        this.sendNotificationAsync = sendNotificationAsync ?? DefaultSendNotificationAsync;
        this.stateChangedHandler = this.OnShellStateChanged;
        ShellInterpreter.Instance.StateChanged += this.stateChangedHandler;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="uri"/> identifies a resource that
    /// the manager actually publishes update notifications for. Subscriptions to
    /// any other URI would never fire and would only consume memory.
    /// </summary>
    internal static bool IsSubscribable(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        foreach (var supported in StateScopedUris)
        {
            if (string.Equals(supported, uri, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Task DefaultSendNotificationAsync(McpSdkServer server, ResourceUpdatedNotificationParams parameters, CancellationToken cancellationToken)
    {
        return server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            parameters,
            cancellationToken: cancellationToken);
    }

    public bool Subscribe(string uri, McpSdkServer server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(server);

        if (!IsSubscribable(uri))
        {
            return false;
        }

        var set = this.subscriptions.GetOrAdd(uri, _ => new ConcurrentDictionary<McpSdkServer, byte>());

        if (!set.ContainsKey(server))
        {
            var perServer = 0;
            foreach (var s in this.subscriptions.Values)
            {
                if (s.ContainsKey(server))
                {
                    perServer++;
                }
            }

            if (perServer >= MaxSubscriptionsPerServer)
            {
                return false;
            }
        }

        set[server] = 0;
        this.logger?.LogInformation("MCP resource subscribed: {Uri}", uri);
        return true;
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
                await this.sendNotificationAsync(server, parameters, cancellationToken).ConfigureAwait(false);
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
