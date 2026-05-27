// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System;
using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Validates the Origin and Host headers on incoming requests to prevent DNS rebinding attacks.
/// Per MCP spec (2025-11-25), servers MUST validate the Origin header and respond
/// with HTTP 403 Forbidden if the Origin is present and invalid. The Host header is
/// also validated to defend against attackers tricking a browser into resolving a
/// non-loopback name to 127.0.0.1; a malformed/non-loopback Host yields 400 Bad Request,
/// since the request itself is structurally invalid for a local-only server.
/// </summary>
internal static class OriginValidationMiddleware
{
    private static readonly string[] AllowedHosts =
    [
        "localhost",
        "127.0.0.1",
        "[::1]",
        "::1",
    ];

    public static IApplicationBuilder UseOriginValidation(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && !IsAllowedOrigin(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var host = context.Request.Headers.Host.ToString();
            if (!IsAllowedHost(host))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next();
        });
    }

    internal static bool IsAllowedOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return IsLoopbackHost(uri.Host);
    }

    internal static bool IsAllowedHost(string host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        // The Host header has the form "host" or "host:port". Parse via Uri to handle
        // bracketed IPv6 literals (e.g. "[::1]:6128") consistently with Origin handling.
        if (!Uri.TryCreate($"http://{host}", UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        // Reject host headers that include a path/query/fragment beyond the authority.
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return IsLoopbackHost(uri.Host);
    }

    private static bool IsLoopbackHost(string host)
    {
        foreach (var allowed in AllowedHosts)
        {
            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IPAddress.IsLoopback(ip);
        }

        return false;
    }
}
