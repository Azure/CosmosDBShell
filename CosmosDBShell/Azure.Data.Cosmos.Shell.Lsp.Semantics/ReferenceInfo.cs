// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp.Semantics;

/// <summary>
/// Represents a single occurrence (definition or usage) of a <see cref="Symbol"/> in source text.
/// Collected during semantic analysis to enable "go to definition", "find references",
/// rename refactoring, and highlight-on-cursor features.
/// </summary>
public sealed class ReferenceInfo
{
    /// <summary>
    /// Gets the symbol which reference is associated with. All references to the same logical entity
    /// share the same <see cref="Symbol"/> instance.
    /// </summary>
    public required Symbol Symbol { get; init; }

    /// <summary>
    /// Gets the zero-based absolute character offset in the document where this reference begins.
    /// </summary>
    public required int Start { get; init; }

    /// <summary>
    /// Gets the length in characters of the referenced span. May be zero for synthetic or insertion positions,
    /// but typically equals the identifier token length.
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// Gets a value that is true if this occurrence is the defining declaration of the symbol (its primary introduction
    /// in the current semantic scope); false if it is a subsequent usage.
    /// </summary>
    public bool IsDefinition { get; init; }
}