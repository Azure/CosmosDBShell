//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Collections.Generic;

/// <summary>
/// Structured evaluation of a query execution plan derived from index metrics and
/// server-side query metrics. Pure data so it can be produced and asserted in tests.
/// </summary>
/// <param name="FullScan">True when no index contributed to the query.</param>
/// <param name="IndexSeek">True when at least one index was utilized.</param>
/// <param name="IndexHitRatio">The index hit ratio in the range [0,1], when available.</param>
/// <param name="RetrievedDocumentCount">Documents loaded by the engine, when available.</param>
/// <param name="OutputDocumentCount">Documents returned by the query, when available.</param>
/// <param name="UtilizedIndexes">Index specifications that contributed to the query.</param>
/// <param name="PotentialIndexes">Index specifications that could improve the query.</param>
internal sealed record PlanEvaluation(
    bool FullScan,
    bool IndexSeek,
    double? IndexHitRatio,
    long? RetrievedDocumentCount,
    long? OutputDocumentCount,
    IReadOnlyList<string> UtilizedIndexes,
    IReadOnlyList<string> PotentialIndexes);
