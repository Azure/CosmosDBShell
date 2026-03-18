// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp.Semantics;

/// <summary>
/// Severity levels for semantic diagnostics emitted after successful syntactic parsing.
/// </summary>
public enum SemanticDiagnosticSeverity
{
    /// <summary>
    /// Informational message (non-actionable, purely descriptive).
    /// </summary>
    Info,

    /// <summary>
    /// Potential issue that does not prevent execution (e.g., unused variable).
    /// </summary>
    Warning,

    /// <summary>
    /// Definitive semantic error (e.g., unknown command, unresolved symbol).
    /// </summary>
    Error,
}

/// <summary>
/// Represents a semantic diagnostic produced during semantic analysis (post-parse).
/// These diagnostics operate on a valid AST and refer to symbol binding, resolution,
/// type/usage validation, and higher-level consistency checks.
/// </summary>
public sealed class SemanticDiagnostic
{
    /// <summary>
    /// Stable diagnostic identifier (e.g. SEM001). Used for suppression and tooling.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable description of the semantic issue.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Absolute zero-based character offset in the source text where the diagnostic starts.
    /// </summary>
    public required int Start { get; init; }

    /// <summary>
    /// Length in characters of the source span related to this diagnostic.
    /// May be zero for insertion points.
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// Severity classification guiding presentation and potential build impact.
    /// </summary>
    public required SemanticDiagnosticSeverity Severity { get; init; }
}