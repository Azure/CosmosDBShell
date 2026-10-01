// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

/// <summary>
/// Creates and validates the MRTR <c>requestState</c> for destructive-command confirmation.
/// The state binds an approval to the exact command line and shell state version it was
/// requested for, and is signed so a client cannot fabricate or retarget it. Each state
/// carries a nonce that can be read once and expires after <see cref="Lifetime"/>, so a
/// captured answer cannot be replayed.
/// </summary>
internal static class ConfirmationRequestState
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // Per-process key: confirmations do not survive a server restart.
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    // Outstanding nonces and their expiry. A nonce is removed when it is read or has expired.
    private static readonly ConcurrentDictionary<string, DateTimeOffset> PendingNonces = new(StringComparer.Ordinal);

    public static string Create(string commandLine, long stateVersion)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in PendingNonces)
        {
            if (entry.Value <= now)
            {
                PendingNonces.TryRemove(entry);
            }
        }

        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        PendingNonces[nonce] = now + Lifetime;
        var payload = Encoding.UTF8.GetBytes(nonce + "\n" + stateVersion.ToString(CultureInfo.InvariantCulture) + "\n" + commandLine);
        return WebEncoders.Base64UrlEncode(payload) + "." + WebEncoders.Base64UrlEncode(Sign(payload));
    }

    public static bool TryRead(string? requestState, string commandLine, out long stateVersion)
    {
        return TryRead(requestState, commandLine, DateTimeOffset.UtcNow, out stateVersion);
    }

    internal static bool TryRead(string? requestState, string commandLine, DateTimeOffset now, out long stateVersion)
    {
        stateVersion = 0;
        if (string.IsNullOrEmpty(requestState))
        {
            return false;
        }

        var separator = requestState.IndexOf('.');
        if (separator <= 0)
        {
            return false;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = WebEncoders.Base64UrlDecode(requestState[..separator]);
            signature = WebEncoders.Base64UrlDecode(requestState[(separator + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, Sign(payload)))
        {
            return false;
        }

        var parts = Encoding.UTF8.GetString(payload).Split('\n', 3);
        if (parts.Length != 3)
        {
            return false;
        }

        // Any signed state is consumed on first use, whether or not it matches this command.
        if (!PendingNonces.TryRemove(parts[0], out var expiry) || expiry <= now)
        {
            return false;
        }

        return string.Equals(parts[2], commandLine, StringComparison.Ordinal)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out stateVersion);
    }

    private static byte[] Sign(byte[] payload) => HMACSHA256.HashData(Key, payload);
}
