// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using System.Text.Json;
using global::Azure.Core;

internal sealed class StaticTokenCredential : TokenCredential
{
    private readonly AccessToken accessToken;

    public StaticTokenCredential(string token)
    {
        var parsed = TryParseJwtExpiry(token);
        this.HasJwtExpiry = parsed.HasValue;
        var expiresOn = parsed ?? DateTimeOffset.UtcNow.AddHours(1);
        this.accessToken = new AccessToken(token, expiresOn);
    }

    internal bool HasJwtExpiry { get; }

    internal DateTimeOffset ExpiresOn => this.accessToken.ExpiresOn;

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return this.accessToken;
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new ValueTask<AccessToken>(this.accessToken);
    }

    internal static DateTimeOffset? TryParseJwtExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var payload = parts[1];

        // Base64url → Base64: replace URL-safe chars and pad
        payload = payload.Replace('-', '+').Replace('_', '/');
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
            if (doc.RootElement.TryGetProperty("exp", out var expElement) &&
                expElement.TryGetInt64(out var exp))
            {
                return DateTimeOffset.FromUnixTimeSeconds(exp);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
