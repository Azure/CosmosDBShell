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
/// Represents a document in the workspace with parsing state.
/// </summary>
public class WorkspaceDocument(DocumentUri uri, string content, int version)
{
    private readonly object parseLock = new();

    /// <summary>
    /// Gets the URI of the document.
    /// </summary>
    public DocumentUri Uri { get; } = uri;

    /// <summary>
    /// Gets the content of the document.
    /// </summary>
    public string Content { get; private set; } = content;

    /// <summary>
    /// Gets the version of the document.
    /// </summary>
    public int Version { get; private set; } = version;

    /// <summary>
    /// Gets the last parse result of the document.
    /// </summary>
    public ParseResult? LastParseResult { get; private set; }

    /// <summary>
    /// Gets the list of diagnostics for the document.
    /// </summary>
    public List<Diagnostic> Diagnostics { get; } = new();

    public Azure.Data.Cosmos.Shell.Lsp.Semantics.SemanticModel? SemanticModel { get; private set; }

    /// <summary>
    /// Updates the document content and triggers parsing.
    /// </summary>
    /// <param name="content">The new content of the document.</param>
    /// <param name="version">The version number of the document.</param>
    public void Update(string content, int version)
    {
        lock (this.parseLock)
        {
            this.Content = content;
            this.Version = version;
            this.Parse();
        }
    }

    /// <summary>
    /// Parses the document content and updates diagnostics.
    /// </summary>
    public void Parse()
    {
        lock (this.parseLock)
        {
            this.Diagnostics.Clear();

            try
            {
                var lexer = new Lexer(this.Content);
                var parser = new StatementParser(lexer);
                var statements = parser.ParseStatements();

                this.LastParseResult = new ParseResult
                {
                    Statements = statements,
                    Comments = lexer.Comments,
                    Success = true,
                    Errors = lexer.Errors,
                };

                // Convert errors to diagnostics
                foreach (var err in lexer.Errors)
                {
                    this.Diagnostics.Add(ToDiagnostic(err, this.Content));
                }

                var analyzer = new Azure.Data.Cosmos.Shell.Lsp.Semantics.SemanticAnalyzer();
                this.SemanticModel = analyzer.Analyze(statements, this.Content);
                foreach (var sdiag in this.SemanticModel.Diagnostics)
                {
                    var (sl, sc) = ToLineColumn(this.Content, sdiag.Start);
                    var (el, ec) = ToLineColumn(this.Content, sdiag.Start + Math.Max(0, sdiag.Length - 1));
                    if (sdiag.Length == 0)
                    {
                        el = sl;
                        ec = sc + 1;
                    }

                    this.Diagnostics.Add(new Diagnostic
                    {
                        Range = new Range(new Position(sl, sc), new Position(el, ec)),
                        Severity = sdiag.Severity switch
                        {
                            Azure.Data.Cosmos.Shell.Lsp.Semantics.SemanticDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                            Azure.Data.Cosmos.Shell.Lsp.Semantics.SemanticDiagnosticSeverity.Info => DiagnosticSeverity.Hint,
                            _ => DiagnosticSeverity.Error,
                        },
                        Code = sdiag.Code,
                        Source = "cosmosshell-sem",
                        Message = sdiag.Message,
                    });
                }
            }
            catch (Exception ex)
            {
                this.LastParseResult = new ParseResult
                {
                    Success = false,
                };

                // Create diagnostic from parse error
                var diagnostic = new Diagnostic
                {
                    Range = this.GetErrorRange(ex),
                    Severity = DiagnosticSeverity.Error,
                    Code = "CS001",
                    Source = "cosmosshell",
                    Message = ex.Message,
                };

                this.Diagnostics.Add(diagnostic);
            }
        }
    }

    private static Diagnostic ToDiagnostic(ParseError error, string content)
    {
        var (startLine, startCol) = ToLineColumn(content, error.Start);
        var (endLine, endCol) = ToLineColumn(content, error.Start + Math.Max(0, error.Length - 1));
        if (error.Length == 0)
        {
            endLine = startLine;
            endCol = startCol + 1;
        }

        return new Diagnostic
        {
            Range = new Range(new Position(startLine, startCol), new Position(endLine, endCol)),
            Severity = error.ErrorLevel == ErrorLevel.Warning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            Code = "CSPARSE",
            Source = "cosmosshell",
            Message = error.Message,
        };
    }

    private static (int Line, int Column) ToLineColumn(string content, int absolute)
    {
        absolute = Math.Clamp(absolute, 0, content.Length);
        int line = 0;
        int lastNl = -1;
        for (int i = 0; i < absolute; i++)
        {
            if (content[i] == '\n')
            {
                line++;
                lastNl = i;
            }
        }

        int column = absolute - (lastNl + 1);
        return (line, column);
    }

    /// <summary>
    /// Gets the error range from the exception.
    /// </summary>
    /// <param name="ex">The exception containing error information.</param>
    /// <returns>The range in the document where the error occurred.</returns>
    private Range GetErrorRange(Exception ex)
    {
        // Try to extract position from exception
        // For now, default to start of document
        return new Range(new Position(0, 0), new Position(0, 10));
    }
}
