// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using System.Text.Json;

/// <summary>
/// Helpers for reading claims from a JWT access token without validating its signature.
/// Used for local identity introspection only (for example the <c>whoami</c> command);
/// the token is never trusted for authorization decisions.
/// </summary>
internal static class JwtClaims
{
    /// <summary>
    /// Decodes the payload segment of a JWT and returns its claims as a JSON element.
    /// Returns <c>null</c> when the token is not a well-formed JWT.
    /// </summary>
    /// <param name="token">The raw JWT access token.</param>
    /// <returns>The decoded payload claims, or <c>null</c> when decoding fails.</returns>
    public static JsonElement? TryDecodePayload(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }

        byte[] jsonBytes;
        try
        {
            jsonBytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonBytes);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a string claim from decoded JWT payload claims, trying each candidate name in order.
    /// </summary>
    /// <param name="claims">The decoded JWT payload, or <c>null</c>.</param>
    /// <param name="names">The claim names to try, in priority order.</param>
    /// <returns>The first matching non-empty string claim, or <c>null</c>.</returns>
    public static string? GetString(JsonElement? claims, params string[] names)
    {
        if (claims is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        return null;
    }
}
