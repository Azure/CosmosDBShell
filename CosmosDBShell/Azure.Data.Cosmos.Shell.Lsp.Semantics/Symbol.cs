// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp.Semantics;

/// <summary>
/// Kinds of symbols recognized by the semantic analysis layer.
/// </summary>
public enum SymbolKind
{
    /// <summary>
    /// A variable (user-defined or implicit) introduced in the current scope.
    /// </summary>
    Variable,

    /// <summary>
    /// A shell command (built-in or dynamically registered).
    /// </summary>
    Command,

    /// <summary>
    /// A user-defined function (future extension / placeholder).
    /// </summary>
    Function,

    /// <summary>
    /// A parameter to a function or command (future extension / placeholder).
    /// </summary>
    Parameter,
}

/// <summary>
/// Base abstraction for all semantic symbols. A symbol represents a unique logical
/// entity (command, variable, function, etc.) discovered during semantic analysis.
/// </summary>
public abstract class Symbol
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Symbol"/> class.
    /// </summary>
    /// <param name="name">The textual name of the symbol as it appears in source.</param>
    /// <param name="kind">The kind of symbol.</param>
    /// <param name="start">Absolute zero-based character offset of the primary declaration.</param>
    /// <param name="length">Length in characters of the declared identifier.</param>
    protected Symbol(string name, SymbolKind kind, int start, int length)
    {
        this.Name = name;
        this.Kind = kind;
        this.Start = start;
        this.Length = length;
    }

    /// <summary>
    /// Gets the symbol's simple (unqualified) name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the classification of this symbol.
    /// </summary>
    public SymbolKind Kind { get; }

    /// <summary>
    /// Gets the absolute zero-based offset where the defining occurrence starts.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the length in characters of the defining occurrence.
    /// </summary>
    public int Length { get; }
}
