// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Microsoft.Azure.Cosmos;

/// <summary>
/// Resolves the provisioned throughput requested by <c>mkdb</c> and <c>mkcon</c>.
/// </summary>
internal static class CreationThroughput
{
    internal const int DefaultMaxRu = 1000;

    internal static bool IsSpecified(string? scale, int? maxRu) => !string.IsNullOrWhiteSpace(scale) || maxRu.HasValue;

    internal static bool IsManual(string? scale) =>
        string.Equals(scale, "manual", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scale, "m", StringComparison.OrdinalIgnoreCase);

    internal static ThroughputProperties CreateProperties(string? scale, int? maxRu)
    {
        var ru = maxRu ?? DefaultMaxRu;
        return IsManual(scale)
            ? ThroughputProperties.CreateManualThroughput(ru)
            : ThroughputProperties.CreateAutoscaleThroughput(ru);
    }
}
