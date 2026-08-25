// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// A single throughput bucket limit configured on a container: the bucket id (1-5)
/// and the maximum percentage of the container's provisioned throughput that requests
/// tagged with that bucket may consume.
/// </summary>
internal sealed record ThroughputBucketView(int Id, int MaxThroughputPercentage);
