// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

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
    // Upper bound on tracked nonces. Beyond it the oldest is dropped, so its confirmation must be repeated.
    internal const int MaxPending = 1024;

    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // Per-process key: confirmations do not survive a server restart.
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    private static readonly object Sync = new();

    // Outstanding nonces and their expiry. A nonce is removed when it is read, expires, or is evicted.
    private static readonly Dictionary<string, DateTimeOffset> PendingNonces = new(StringComparer.Ordinal);

    // Nonces in creation order. All share one lifetime, so the oldest expires first and pruning stops at the
    // first live entry. Entries already read stay queued until pruned; the queue never exceeds MaxPending.
    private static readonly Queue<(string Nonce, DateTimeOffset Expiry)> NonceOrder = new();

    public static string Create(string commandLine, long stateVersion)
    {
        var now = DateTimeOffset.UtcNow;
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        lock (Sync)
        {
            while (NonceOrder.TryPeek(out var oldest) && (oldest.Expiry <= now || NonceOrder.Count >= MaxPending))
            {
                NonceOrder.Dequeue();
                PendingNonces.Remove(oldest.Nonce);
            }

            PendingNonces[nonce] = now + Lifetime;
            NonceOrder.Enqueue((nonce, now + Lifetime));
        }

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
        DateTimeOffset expiry;
        lock (Sync)
        {
            if (!PendingNonces.Remove(parts[0], out expiry))
            {
                return false;
            }
        }

        if (expiry <= now)
        {
            return false;
        }

        return string.Equals(parts[2], commandLine, StringComparison.Ordinal)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out stateVersion);
    }

    private static byte[] Sign(byte[] payload) => HMACSHA256.HashData(Key, payload);
}
