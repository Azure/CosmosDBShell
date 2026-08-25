// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// The throughput bucket configuration for a container, including the container's
/// effective provisioning mode and the per-bucket limits currently defined.
/// </summary>
internal sealed record ThroughputBucketsView(
    string ResourceName,
    bool IsAutoscale,
    int? Throughput,
    int? AutoscaleMaxThroughput,
    IReadOnlyList<ThroughputBucketView> Buckets);
