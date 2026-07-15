// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core
{
    /// <summary>
    /// Represents a saved connection profile (non-secret information only).
    /// </summary>
    public class ConnectionProfile
    {
        public string Endpoint { get; set; } = string.Empty;

        public string? Mode { get; set; }
    }
}
