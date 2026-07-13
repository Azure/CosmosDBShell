// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Snapshot of a container's conflict resolution policy. <paramref name="Mode"/> is
/// either <c>LastWriterWins</c> or <c>Custom</c>. For last-writer-wins policies
/// <paramref name="ResolutionPath"/> names the property used to pick a winner; for
/// custom policies <paramref name="ResolutionProcedure"/> names the stored procedure
/// that resolves conflicts.
/// </summary>
internal sealed record ConflictResolutionView(string Mode, string? ResolutionPath, string? ResolutionProcedure);
