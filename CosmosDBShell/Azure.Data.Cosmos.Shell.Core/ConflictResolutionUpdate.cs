// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// A fully-resolved conflict resolution policy to apply to a container.
/// <paramref name="Mode"/> is either <c>LastWriterWins</c> or <c>Custom</c>. For
/// last-writer-wins policies <paramref name="ResolutionPath"/> is set and
/// <paramref name="ResolutionProcedure"/> is null; for custom policies the reverse
/// holds. Callers are expected to merge partial changes against the current policy
/// before constructing this record.
/// </summary>
internal sealed record ConflictResolutionUpdate(string Mode, string? ResolutionPath, string? ResolutionProcedure);
