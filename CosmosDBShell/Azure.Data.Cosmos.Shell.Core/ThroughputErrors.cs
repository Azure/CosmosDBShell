// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal static class ThroughputErrors
{
    internal static bool IsServerlessThroughputError(string? message)
    {
        return message is not null
            && message.Contains("serverless", StringComparison.OrdinalIgnoreCase);
    }
}
