// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp.Semantics;

/// <summary>
/// Represents the semantic analysis result for a single parsed document.
/// It groups discovered symbols, their occurrences (references), and any
/// semantic diagnostics (e.g., unknown commands, unresolved variables).
/// </summary>
public sealed class SemanticModel
{
    /// <summary>
    /// Gets the immutable list of all symbols declared or inferred in the document.
    /// Each symbol instance is unique per logical entity (e.g., a variable name).
    /// </summary>
    public IReadOnlyList<Symbol> Symbols { get; init; } = Array.Empty<Symbol>();

    /// <summary>
    /// Gets the immutable list of all references (definitions and usages) to symbols.
    /// Used for features like "find references", rename, and highlighting.
    /// </summary>
    public IReadOnlyList<ReferenceInfo> References { get; init; } = Array.Empty<ReferenceInfo>();

    /// <summary>
    /// Gets the immutable list of semantic diagnostics produced during analysis.
    /// Syntactic (parse) errors are not included here; those originate from the parser.
    /// </summary>
    public IReadOnlyList<SemanticDiagnostic> Diagnostics { get; init; } = Array.Empty<SemanticDiagnostic>();

    /// <summary>
    /// Returns the symbol whose defining span contains the specified absolute position,
    /// or null if no symbol definition covers that point.
    /// </summary>
    /// <param name="position">Zero-based absolute character offset in the source text.</param>
    public Symbol? GetSymbolAt(int position)
        => this.Symbols.FirstOrDefault(s => position >= s.Start && position < s.Start + s.Length);

    /// <summary>
    /// Finds all recorded references associated with the given symbol instance.
    /// </summary>
    /// <param name="symbol">The symbol whose references to enumerate.</param>
    /// <returns>All reference entries (including its defining occurrence).</returns>
    public IEnumerable<ReferenceInfo> FindReferences(Symbol symbol)
        => this.References.Where(r => ReferenceEquals(r.Symbol, symbol));
}