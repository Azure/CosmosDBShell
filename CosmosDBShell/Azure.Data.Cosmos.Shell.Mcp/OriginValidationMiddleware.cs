// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System;
using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Validates the Origin header on incoming requests to prevent DNS rebinding attacks.
/// Per MCP spec (2025-11-25), servers MUST validate the Origin header and respond
/// with HTTP 403 Forbidden if the Origin is present and invalid.
/// </summary>
internal static class OriginValidationMiddleware
{
    private static readonly string[] AllowedHosts =
    [
        "localhost",
        "127.0.0.1",
        "[::1]",
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

        var host = uri.Host;
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
