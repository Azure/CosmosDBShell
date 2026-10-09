// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

internal sealed class LegacyHttpAdmission
{
    private readonly object sync = new();
    private readonly Dictionary<(string SessionId, RequestId RequestId), Admission> outstanding = [];

    internal int OutstandingRequests
    {
        get
        {
            lock (this.sync)
            {
                return this.outstanding.Count;
            }
        }
    }

    internal async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path != "/message" || !HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        JsonRpcMessage? message;
        try
        {
            message = await context.Request.ReadFromJsonAsync<JsonRpcMessage>(
                McpJsonUtilities.DefaultOptions, context.RequestAborted);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Invalid JSON-RPC message.", context.RequestAborted);
            return;
        }

        context.Request.Body.Position = 0;
        if (message is not JsonRpcRequest request)
        {
            await next(context);
            return;
        }

        var key = (context.Request.Query["sessionId"].ToString(), request.Id);
        Admission? admission = null;
        lock (this.sync)
        {
            if (this.outstanding.Count < LegacyRequestLimiter.MaxOutstandingRequests && !this.outstanding.ContainsKey(key))
            {
                admission = new Admission(this, key);
                this.outstanding.Add(key, admission);
            }
        }

        if (admission is null)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "1";
            await context.Response.WriteAsync(
                "Legacy SSE is busy or this request ID is already outstanding. Retry after an outstanding request completes.",
                context.RequestAborted);
            return;
        }

        try
        {
            await next(context);
        }
        finally
        {
            // Accepted requests retain admission until the SSE response is written or the session ends.
            if (context.Response.StatusCode != StatusCodes.Status202Accepted)
            {
                admission.Dispose();
            }
        }
    }

    internal McpMessageHandler TrackIncoming(McpMessageHandler next)
    {
        return async (context, cancellationToken) =>
        {
            var admission = context.JsonRpcMessage is JsonRpcRequest request
                ? this.Find(context.Server.SessionId, request.Id)
                : null;
            try
            {
                await next(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                admission?.Dispose();
                throw;
            }
        };
    }

    internal McpMessageHandler TrackOutgoing(McpMessageHandler next)
    {
        return async (context, cancellationToken) =>
        {
            using var admission = context.JsonRpcMessage switch
            {
                JsonRpcResponse response => this.Find(context.Server.SessionId, response.Id),
                JsonRpcError error => this.Find(context.Server.SessionId, error.Id),
                _ => null,
            };
            await next(context, cancellationToken);
        };
    }

    internal void EndSession(string? sessionId)
    {
        lock (this.sync)
        {
            foreach (var key in this.outstanding.Keys.Where(key => key.SessionId == sessionId).ToArray())
            {
                this.outstanding.Remove(key);
            }
        }
    }

    private Admission? Find(string? sessionId, RequestId requestId)
    {
        lock (this.sync)
        {
            return sessionId is not null && this.outstanding.TryGetValue((sessionId, requestId), out var admission)
                ? admission
                : null;
        }
    }

    private sealed class Admission(LegacyHttpAdmission owner, (string SessionId, RequestId RequestId) key) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.sync)
            {
                if (owner.outstanding.TryGetValue(key, out var current) && ReferenceEquals(current, this))
                {
                    owner.outstanding.Remove(key);
                }
            }
        }
    }
}
