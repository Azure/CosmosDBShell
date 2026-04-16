// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

internal static class ResultLimit
{
    public const int DefaultMaxItemCount = 100;

    public static int? ResolveMaxItemCount(int? requestedMax, int? defaultMaxItemCount = DefaultMaxItemCount)
    {
        if (!requestedMax.HasValue)
        {
            return defaultMaxItemCount;
        }

        return requestedMax.Value <= 0 ? null : requestedMax.Value;
    }

    public static bool IsLimitReached(int count, int? effectiveMaxItemCount)
    {
        return effectiveMaxItemCount.HasValue && count >= effectiveMaxItemCount.Value;
    }
}