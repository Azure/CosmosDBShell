// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal class FoldingRangeHandler : IFoldingRangeHandler
{
    private readonly CosmosShellWorkspace workspace;

    public FoldingRangeHandler(CosmosShellWorkspace workspace)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    // Old (parameterless) registration kept because the interface provides both overloads in this codebase pattern.
    public FoldingRangeRegistrationOptions GetRegistrationOptions() =>
        new FoldingRangeRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("cosmosshell"),
        };

    public FoldingRangeRegistrationOptions GetRegistrationOptions(FoldingRangeCapability capability, ClientCapabilities clientCapabilities) =>
        new FoldingRangeRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("cosmosshell"),
        };

    public Task<Container<FoldingRange>?> Handle(FoldingRangeRequestParam request, CancellationToken cancellationToken)
    {
        var doc = this.workspace.GetDocument(request.TextDocument.Uri);
        if (doc?.Content == null)
        {
            return Task.FromResult<Container<FoldingRange>?>(null);
        }

        var text = doc.Content;
        var ranges = new List<FoldingRange>();

        // Precompute line offsets for quick mapping (line -> absolute start index)
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        // Stack of (line, character) for '{'
        var stack = new Stack<(int Line, int Character)>();

        int currentLine = 0;
        int currentCharInLine = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var ch = text[i];
            if (ch == '{')
            {
                stack.Push((currentLine, currentCharInLine));
            }
            else if (ch == '}' && stack.Count > 0)
            {
                var open = stack.Pop();
                var closeLine = currentLine;

                // Only create a folding range if it spans multiple lines
                if (closeLine > open.Line)
                {
                    // By convention end line is the line containing the closing brace; many editors fold excluding that line visually.
                    ranges.Add(new FoldingRange
                    {
                        StartLine = open.Line,
                        StartCharacter = open.Character,
                        EndLine = closeLine,
                        EndCharacter = currentCharInLine + 1, // position after '}'
                        Kind = FoldingRangeKind.Region,
                    });
                }
            }

            if (ch == '\n')
            {
                currentLine++;
                currentCharInLine = 0;
            }
            else if (ch != '\r')
            {
                currentCharInLine++;
            }
        }

        // Optionally sort ranges by start (not strictly required but nice for determinism)
        ranges.Sort((a, b) =>
        {
            var c = a.StartLine.CompareTo(b.StartLine);
            return c != 0 ? c : a.StartCharacter.GetValueOrDefault().CompareTo(b.StartCharacter.GetValueOrDefault());
        });

        return Task.FromResult<Container<FoldingRange>?>(new Container<FoldingRange>(ranges));
    }
}