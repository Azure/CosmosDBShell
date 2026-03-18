// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System.Collections.Concurrent;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Result of parsing a document.
/// </summary>
public class ParseResult
{
    /// <summary>
    /// Gets the list of parsed statements in the document.
    /// </summary>
    public IReadOnlyList<Statement> Statements { get; init; } = [];

    /// <summary>
    /// Gets the list of comment tokens in the document.
    /// </summary>
    public IReadOnlyList<Token> Comments { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the parsing was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the list of parse errors encountered during parsing.
    /// </summary>
    public IReadOnlyList<ParseError> Errors { get; init; } = Array.Empty<ParseError>();
}
