// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal sealed record ContainerSettingsView(
    string ContainerName,
    IReadOnlyList<string> PartitionKeyPaths,
    long? AnalyticalStorageTtl,
    int? MinThroughput,
    int? MaxThroughput,
    ThroughputAvailability Throughput,
    string? ThroughputErrorMessage);
