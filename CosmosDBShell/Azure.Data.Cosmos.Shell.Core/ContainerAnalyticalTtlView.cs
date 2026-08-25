// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Snapshot of a container's analytical store time-to-live (TTL) configuration. Cosmos DB
/// models analytical TTL as a tri-state:
/// <list type="bullet">
/// <item><description><c>null</c> or <c>0</c> — the analytical store is disabled and no
/// analytical copy is retained.</description></item>
/// <item><description><c>-1</c> — the analytical store is enabled and data is retained
/// indefinitely.</description></item>
/// <item><description>positive — analytical data is retained for this many seconds.</description></item>
/// </list>
/// </summary>
internal sealed record ContainerAnalyticalTtlView(long? AnalyticalTimeToLiveSeconds);
