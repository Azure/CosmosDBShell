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

    // Narrower than IsServerlessThroughputError: creation retries without throughput, so an
    // unrelated serverless failure must not be retried with a different request.
    internal static bool IsServerlessCreationThroughputError(string? message)
    {
        return IsServerlessThroughputError(message)
            && (message!.Contains("throughput", StringComparison.OrdinalIgnoreCase)
                || message.Contains("autopilot", StringComparison.OrdinalIgnoreCase)
                || message.Contains("offer", StringComparison.OrdinalIgnoreCase));
    }
}
