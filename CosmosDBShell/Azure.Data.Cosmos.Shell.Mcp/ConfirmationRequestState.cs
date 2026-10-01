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
/// requested for, and is signed so a client cannot fabricate or retarget it.
/// </summary>
internal static class ConfirmationRequestState
{
    // Per-process key: confirmations do not survive a server restart.
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    public static string Create(string commandLine, long stateVersion)
    {
        var payload = Encoding.UTF8.GetBytes(stateVersion.ToString(CultureInfo.InvariantCulture) + "\n" + commandLine);
        return WebEncoders.Base64UrlEncode(payload) + "." + WebEncoders.Base64UrlEncode(Sign(payload));
    }

    public static bool TryRead(string? requestState, string commandLine, out long stateVersion)
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

        var text = Encoding.UTF8.GetString(payload);
        var newline = text.IndexOf('\n');
        return newline > 0
            && string.Equals(text[(newline + 1)..], commandLine, StringComparison.Ordinal)
            && long.TryParse(text[..newline], NumberStyles.None, CultureInfo.InvariantCulture, out stateVersion);
    }

    private static byte[] Sign(byte[] payload) => HMACSHA256.HashData(Key, payload);
}
