// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System.Collections.Concurrent;

using Azure.Data.Cosmos.Shell.Parser;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Manages the workspace state for the Cosmos Shell Language Server.
/// Tracks open documents, parses content, and provides analysis services.
/// </summary>
internal class CosmosShellWorkspace
{
    private readonly ConcurrentDictionary<DocumentUri, WorkspaceDocument> documents = new();

    /// <summary>
    /// Gets all open documents in the workspace.
    /// </summary>
    public IEnumerable<WorkspaceDocument> Documents => this.documents.Values;

    /// <summary>
    /// Opens or updates a document in the workspace.
    /// </summary>
    public void OpenDocument(DocumentUri uri, string content, int version = 0)
    {
        var document = new WorkspaceDocument(uri, content, version);
        this.documents.AddOrUpdate(uri, document, (_, _) => document);
        document.Parse();
    }

    /// <summary>
    /// Updates an existing document's content.
    /// </summary>
    public void UpdateDocument(DocumentUri uri, string content, int version)
    {
        if (this.documents.TryGetValue(uri, out var document))
        {
            document.Update(content, version);
        }
        else
        {
            this.OpenDocument(uri, content, version);
        }
    }

    /// <summary>
    /// Closes a document and removes it from the workspace.
    /// </summary>
    public void CloseDocument(DocumentUri uri)
    {
        this.documents.TryRemove(uri, out _);
    }

    /// <summary>
    /// Gets a document by URI.
    /// </summary>
    public WorkspaceDocument? GetDocument(DocumentUri uri)
    {
        return this.documents.TryGetValue(uri, out var document) ? document : null;
    }

    /// <summary>
    /// Gets parsing results for a document.
    /// </summary>
    public ParseResult? GetParseResult(DocumentUri uri)
    {
        return this.GetDocument(uri)?.LastParseResult;
    }

    /// <summary>
    /// Gets the word at a specific position in a document.
    /// </summary>
    public string? GetWordAtPosition(DocumentUri uri, Position position)
    {
        var document = this.GetDocument(uri);
        if (document == null)
        {
            return null;
        }

        var lines = document.Content.Split('\n');
        if (position.Line >= lines.Length)
        {
            return null;
        }

        var line = lines[position.Line];
        if (position.Character >= line.Length)
        {
            return null;
        }

        // Find word boundaries
        var start = position.Character;
        var end = position.Character;

        // Move start back to beginning of word
        while (start > 0 && IsWordChar(line[start - 1]))
        {
            start--;
        }

        // Move end forward to end of word
        while (end < line.Length && IsWordChar(line[end]))
        {
            end++;
        }

        return start < end ? line.Substring(start, end - start) : null;
    }

    /// <summary>
    /// Gets the context at a specific position for completion.
    /// </summary>
    public Azure.Data.Cosmos.Shell.Lsp.CompletionContext GetCompletionContext(DocumentUri uri, Position position)
    {
        var document = this.GetDocument(uri);
        if (document == null)
        {
            return CompletionContext.Empty;
        }

        var offset = GetOffset(document.Content, position);
        var textUpToPosition = document.Content.Substring(0, offset);

        // Parse up to the position
        try
        {
            var lexer = new Lexer(textUpToPosition);
            var parser = new StatementParser(lexer);
            var statement = parser.ParseStatement();

            return new Azure.Data.Cosmos.Shell.Lsp.CompletionContext
            {
                TextUpToPosition = textUpToPosition,
                CurrentStatement = statement,
                Position = position,
                Document = document,
            };
        }
        catch
        {
            // If parsing fails, return basic context
            return new Azure.Data.Cosmos.Shell.Lsp.CompletionContext
            {
                TextUpToPosition = textUpToPosition,
                Position = position,
                Document = document,
            };
        }
    }

    private static bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '$';
    }

    private static int GetOffset(string content, Position position)
    {
        var lines = content.Split('\n');
        var offset = 0;

        for (int i = 0; i < position.Line && i < lines.Length; i++)
        {
            offset += lines[i].Length + 1; // +1 for newline
        }

        if (position.Line < lines.Length)
        {
            offset += Math.Min(position.Character, lines[position.Line].Length);
        }

        return offset;
    }
}
