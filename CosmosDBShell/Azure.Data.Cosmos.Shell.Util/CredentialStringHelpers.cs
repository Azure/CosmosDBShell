// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util
{
    public enum Credential
    {
        None,
        DBKey,
        EntraId,
        ManagedIdentity,
    }

    internal static class CredentialStringHelpers
    {
        public static bool TryParseCredential(string credentialString, out Credential credential, out string credentialId)
        {
            credential = Credential.None;
            credentialId = string.Empty;
            if (string.IsNullOrEmpty(credentialString))
            {
                return false;
            }

            var parts = credentialString.Split('=', 2);
            if (parts.Length != 2)
            {
                return false; // Invalid format
            }

            var type = parts[0].ToLowerInvariant();
            var value = parts[1];
            switch (type)
            {
                case "key":
                    credential = Credential.DBKey;
                    break;
                case "tenantid":
                    credential = Credential.EntraId;
                    break;
                case "identity":
                    credential = Credential.ManagedIdentity;
                    break;
                default:
                    return false; // Invalid credential type
            }

            credentialId = value;
            return true;
        }
    }
}