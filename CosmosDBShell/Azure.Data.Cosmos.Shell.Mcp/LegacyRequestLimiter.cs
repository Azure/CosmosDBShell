// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

internal sealed class LegacyRequestLimiter : IDisposable
{
    internal const int MaxOutstandingRequests = 16;
    internal const McpErrorCode OverloadErrorCode = (McpErrorCode)(-32000);

    private readonly SemaphoreSlim permits = new(MaxOutstandingRequests, MaxOutstandingRequests);

    internal int OutstandingRequests => MaxOutstandingRequests - this.permits.CurrentCount;

    internal McpMessageHandler Limit(McpMessageHandler next)
    {
        return async (context, cancellationToken) =>
        {
            // Replies and notifications must remain available for elicitation and cancellation.
            if (context.JsonRpcMessage is not JsonRpcRequest)
            {
                await next(context, cancellationToken);
                return;
            }

            if (!await this.permits.WaitAsync(0, cancellationToken))
            {
                throw new McpProtocolException(
                    $"Legacy SSE is busy: at most {MaxOutstandingRequests} requests may be outstanding. Retry after an outstanding request completes.",
                    OverloadErrorCode);
            }

            try
            {
                // The SDK message pipeline awaits the handler, unlike the legacy HTTP POST.
                await next(context, cancellationToken);
            }
            finally
            {
                this.permits.Release();
            }
        };
    }

    public void Dispose() => this.permits.Dispose();
}
