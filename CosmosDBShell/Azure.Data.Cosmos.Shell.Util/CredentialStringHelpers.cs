// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util
{
    internal static class CredentialStringHelpers
    {
        public static bool TryParseAccountKey(string? envValue, out string accountKey)
        {
            accountKey = string.Empty;
            if (string.IsNullOrEmpty(envValue))
            {
                return false;
            }

            if (envValue.StartsWith("key=", StringComparison.OrdinalIgnoreCase))
            {
                accountKey = envValue.Substring(4);
            }
            else
            {
                accountKey = envValue;
            }

            return !string.IsNullOrEmpty(accountKey);
        }
    }
}