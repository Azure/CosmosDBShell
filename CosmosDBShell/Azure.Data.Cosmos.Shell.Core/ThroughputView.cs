// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal sealed record ThroughputView(
    string Scope,
    string ResourceName,
    bool IsAutoscale,
    int? Throughput,
    int? AutoscaleMaxThroughput,
    int? MinThroughput,
    ThroughputAvailability Availability,
    string? ErrorMessage);
