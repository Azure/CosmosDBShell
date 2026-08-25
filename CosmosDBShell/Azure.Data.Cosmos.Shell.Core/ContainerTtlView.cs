// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Snapshot of a container's default time-to-live (TTL) configuration. Cosmos DB
/// models TTL as a tri-state:
/// <list type="bullet">
/// <item><description><c>null</c> — TTL is disabled and items never expire.</description></item>
/// <item><description><c>-1</c> — TTL is enabled with no container default, so items
/// expire only when they carry their own <c>ttl</c> property.</description></item>
/// <item><description>positive — every item expires after this many seconds unless it
/// overrides the value with its own <c>ttl</c> property.</description></item>
/// </list>
/// </summary>
internal sealed record ContainerTtlView(int? DefaultTimeToLive);
