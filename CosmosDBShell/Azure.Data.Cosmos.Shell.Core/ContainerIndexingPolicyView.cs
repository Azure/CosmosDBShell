// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal sealed record ContainerIndexingPolicyView(
    string IndexingMode,
    bool Automatic,
    int IncludedPathCount,
    int ExcludedPathCount,
    int CompositeIndexCount,
    int SpatialIndexCount,
    int VectorIndexCount);
