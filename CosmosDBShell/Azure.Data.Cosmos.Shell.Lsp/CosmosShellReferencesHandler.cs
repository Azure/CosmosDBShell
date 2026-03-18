// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp;

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Implements basic "find all references" for Cosmos shell scripts,
/// matching either variables (tokens starting with $) or identifier tokens (function names, etc)
/// across all currently open documents in the workspace.
/// </summary>
internal sealed class CosmosShellReferencesHandler : IReferencesHandler
{
    private readonly CosmosShellWorkspace workspace;
    private readonly ILogger<CosmosShellReferencesHandler> logger;

    public CosmosShellReferencesHandler(
        CosmosShellWorkspace workspace,
        ILogger<CosmosShellReferencesHandler> logger)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ReferenceRegistrationOptions GetRegistrationOptions(ReferenceCapability capability, ClientCapabilities clientCapabilities)
        => new ReferenceRegistrationOptions
        {
            DocumentSelector = LspServer.DocumentSelector,
        };

    public async Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        try
        {
            var doc = this.workspace.GetDocument(request.TextDocument.Uri);
            if (doc == null)
            {
                return new LocationContainer();
            }

            var symbol = GetSymbolAtPosition(doc.Content, request.Position);
            if (string.IsNullOrEmpty(symbol))
            {
                return new LocationContainer();
            }

            this.logger.LogDebug("Find references for symbol '{Symbol}' in all open documents.", symbol);

            var locations = new List<Location>();

            foreach (var wdoc in this.workspace.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var range in FindAllOccurrences(wdoc.Content, symbol))
                {
                    locations.Add(new Location
                    {
                        Uri = wdoc.Uri,
                        Range = range,
                    });
                }
            }

            return await Task.FromResult(new LocationContainer(locations));
        }
        catch (OperationCanceledException)
        {
            return new LocationContainer();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error computing references.");
            return new LocationContainer();
        }
    }

    public void SetCapability(ReferenceCapability capability)
    {
        // No client-specific capability information needed.
    }

    private static string? GetSymbolAtPosition(string content, Position position)
    {
        var offset = GetOffset(content, position);
        if (offset < 0 || offset >= content.Length)
        {
            return null;
        }

        // Expand to cover variable or identifier.
        int start = offset;
        int end = offset;

        // If cursor is just after a symbol, step one back (common in editors).
        if (start > 0 && IsIdentifierPart(content[start - 1]))
        {
            start--;
            end--;
        }

        // Include $ if present right before identifier chars (variable).
        if (start > 0 && content[start] != '$' && content[start - 1] == '$')
        {
            start--;
        }

        // Move start backward
        while (start > 0 && IsIdentifierPart(content[start - 1]))
        {
            start--;
        }

        // If variable prefix
        if (start > 0 && content[start - 1] == '$')
        {
            start--;
        }

        // Move end forward
        while (end < content.Length && IsIdentifierPart(content[end]))
        {
            end++;
        }

        if (start == end)
        {
            // Maybe we are on a solitary '$'
            if (content[start] == '$')
            {
                return "$";
            }

            return null;
        }

        var token = content[start..end];

        // Require variable starting with $ or plain identifier
        if (token.StartsWith('$'))
        {
            if (token.Length == 1)
            {
                return null;
            }

            return token;
        }

        // Identifier: letters, digits, underscore
        if (token.All(IsIdentifierPart) && char.IsLetter(token[0]))
        {
            return token;
        }

        return null;
    }

    private static bool IsIdentifierPart(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static int GetOffset(string content, Position position)
    {
        int line = 0;
        int idx = 0;
        while (line < position.Line && idx < content.Length)
        {
            if (content[idx] == '\n')
            {
                line++;
            }

            idx++;
        }

        if (line != position.Line)
        {
            return -1;
        }

        return idx + position.Character;
    }

    private static IEnumerable<Range> FindAllOccurrences(string content, string symbol)
    {
        // Simple text scan with boundary checks
        int index = 0;
        while (index < content.Length)
        {
            index = content.IndexOf(symbol, index, StringComparison.Ordinal);
            if (index < 0)
            {
                yield break;
            }

            if (IsWholeToken(content, index, symbol.Length))
            {
                yield return ToRange(content, index, index + symbol.Length);
            }

            index += symbol.Length;
        }
    }

    private static bool IsWholeToken(string content, int start, int length)
    {
        // For variables beginning with $, boundary is only needed after the token.
        bool leftOk = (start == 0) || !IsIdentifierPart(content[start - 1]);
        int end = start + length;
        bool rightOk = (end >= content.Length) || !IsIdentifierPart(content[end]);

        return leftOk && rightOk;
    }

    private static Range ToRange(string content, int absStart, int absEnd)
    {
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        (int Line, int Col) ToLineCol(int absolute)
        {
            int line = lineStarts.BinarySearch(absolute);
            if (line < 0)
            {
                line = ~line - 1;
            }

            int col = absolute - lineStarts[line];
            return (line, col);
        }

        var (sl, sc) = ToLineCol(absStart);
        var (el, ec) = ToLineCol(absEnd);
        return new Range(new Position(sl, sc), new Position(el, ec));
    }
}