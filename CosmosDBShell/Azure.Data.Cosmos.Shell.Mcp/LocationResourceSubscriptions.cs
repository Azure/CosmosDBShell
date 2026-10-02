// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.Text.Json;
using System.Text.Json.Nodes;
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

    // Open subscriptions/listen streams (2026-07-28). Each lives as long as its listen request.
    private readonly HashSet<ListenStream> listenStreams = [];

    // Listen requests are held-open POSTs, which the transport does not end on shutdown. They must end
    // on ApplicationStopping: the web server stops before this service and waits for open requests.
    private readonly CancellationTokenSource stopping = new();

    private readonly CancellationTokenRegistration stoppingRegistration;

    // Notifications carry only the URI, so pending changes are coalesced into one.
    private readonly Channel<bool> changes = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly ILogger<LocationResourceSubscriptions> logger;

    public LocationResourceSubscriptions(ILogger<LocationResourceSubscriptions> logger, IHostApplicationLifetime? lifetime = null)
    {
        this.logger = logger;
        this.stoppingRegistration = lifetime?.ApplicationStopping.Register(this.stopping.Cancel) ?? default;
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

    internal int ListenerCount
    {
        get
        {
            lock (this.sync)
            {
                return this.listenStreams.Count;
            }
        }
    }

    /// <summary>
    /// Runs an MCP session and keeps it available for notifications until the session ends.
    /// </summary>
    public async Task RunSessionAsync(ModelContextProtocol.Server.McpServer server, CancellationToken cancellationToken)
    {
        // Stateless 2026-07-28 requests have no session to register.
        var sessionId = string.IsNullOrEmpty(server.SessionId) ? null : server.SessionId;
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
        if (string.IsNullOrEmpty(sessionId))
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
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        lock (this.sync)
        {
            this.subscribedSessionIds.Remove(sessionId);
        }
    }

    /// <summary>
    /// Handles a <c>subscriptions/listen</c> request: acknowledges the honored filters and streams
    /// current-location updates over the request until it is cancelled.
    /// </summary>
    public async ValueTask<EmptyResult> ListenAsync(RequestContext<SubscriptionsListenRequestParams> request, CancellationToken cancellationToken)
    {
        // Only the current-location resource supports updates; tools and resources lists are static.
        var honorsLocation = request.Params?.Notifications?.ResourceSubscriptions?.Contains(ResourceOperations.CurrentLocationUri, StringComparer.Ordinal) == true;
        var stream = new ListenStream(request.Server, request.JsonRpcRequest.Id);

        var acknowledgement = JsonSerializer.SerializeToNode(
            new SubscriptionsAcknowledgedNotificationParams
            {
                Notifications = new SubscriptionsListenNotifications
                {
                    ResourceSubscriptions = honorsLocation ? [ResourceOperations.CurrentLocationUri] : null,
                },
            },
            McpJsonUtilities.DefaultOptions)!.AsObject();
        acknowledgement["_meta"] = stream.CreateMeta();
        using var listenCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.stopping.Token);
        if (!honorsLocation)
        {
            await SendAcknowledgementAsync(request.Server, acknowledgement, listenCancellation.Token);
            return new EmptyResult();
        }

        // Register before acknowledging so no change is missed. Changes seen before the acknowledgement
        // is sent wait in the stream's queue, which is read only after the acknowledgement.
        lock (this.sync)
        {
            this.listenStreams.Add(stream);
        }

        try
        {
            await SendAcknowledgementAsync(request.Server, acknowledgement, listenCancellation.Token);

            // Each listener sends its own updates, so a slow or stalled stream delays only itself.
            while (await stream.Updates.Reader.WaitToReadAsync(listenCancellation.Token))
            {
                stream.Updates.Reader.TryRead(out _);
                try
                {
                    await stream.SendUpdateAsync(listenCancellation.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    this.logger.LogWarning(ex, "Could not notify an MCP client about the shell location change.");
                }
            }
        }
        catch (OperationCanceledException) when (listenCancellation.IsCancellationRequested)
        {
            // Cancellation is the normal end of a listen stream.
        }
        finally
        {
            lock (this.sync)
            {
                this.listenStreams.Remove(stream);
            }
        }

        return new EmptyResult();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await this.stopping.CancelAsync();
        await base.StopAsync(cancellationToken);
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
                foreach (var stream in this.listenStreams)
                {
                    stream.Updates.Writer.TryWrite(true);
                }
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
        this.stoppingRegistration.Dispose();
        this.stopping.Dispose();
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

    private static Task SendAcknowledgementAsync(ModelContextProtocol.Server.McpServer server, JsonObject acknowledgement, CancellationToken cancellationToken)
    {
        return server.SendMessageAsync(
            new JsonRpcNotification { Method = NotificationMethods.SubscriptionsAcknowledgedNotification, Params = acknowledgement },
            cancellationToken);
    }

    private void OnLocationChanged()
    {
        this.changes.Writer.TryWrite(true);
    }

    private sealed class ListenStream(ModelContextProtocol.Server.McpServer server, RequestId id)
    {
        public ModelContextProtocol.Server.McpServer Server { get; } = server;

        // Notifications carry only the URI, so pending changes for this listener are coalesced into one.
        public Channel<bool> Updates { get; } = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = true });

        public Task SendUpdateAsync(CancellationToken cancellationToken)
        {
            return this.Server.SendNotificationAsync(
                NotificationMethods.ResourceUpdatedNotification,
                new ResourceUpdatedNotificationParams { Uri = ResourceOperations.CurrentLocationUri, Meta = this.CreateMeta() },
                cancellationToken: cancellationToken);
        }

        // Notifications on a listen stream are tagged with the listen request ID so clients can demultiplex them.
        public JsonObject CreateMeta() => new()
        {
            [MetaKeys.SubscriptionId] = id.Id switch
            {
                string stringId => JsonValue.Create(stringId),
                long longId => JsonValue.Create(longId),
                _ => null,
            },
        };
    }
}
