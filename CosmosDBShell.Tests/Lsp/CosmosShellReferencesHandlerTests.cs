// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Lsp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp;
using Microsoft.Extensions.Logging.Abstractions;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using Xunit;

using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

public class CosmosShellReferencesHandlerTests : IDisposable
{
    private readonly CosmosShellWorkspace workspace;
    private readonly CosmosShellReferencesHandler handler;
    private readonly DocumentUri uri1 = DocumentUri.From("file:///doc1.csh");
    private readonly DocumentUri uri2 = DocumentUri.From("file:///doc2.csh");
    private readonly DocumentUri uri3 = DocumentUri.From("file:///doc3.csh");

    public CosmosShellReferencesHandlerTests()
    {
        this.workspace = new CosmosShellWorkspace();
        this.handler = new CosmosShellReferencesHandler(this.workspace, NullLogger<CosmosShellReferencesHandler>.Instance);
        _ = ShellInterpreter.Instance; // Ensure shell initialized (if needed by workspace)
    }

    public void Dispose()
    {
        // Nothing disposable currently in workspace, method left for symmetry / future extension
    }

    private static string ExtractRangeText(string content, Range range)
    {
        var lines = content.Split('\n');
        var startLine = (int)range.Start.Line;
        var endLine = (int)range.End.Line;
        if (startLine == endLine)
        {
            return lines[startLine].Substring((int)range.Start.Character, (int)(range.End.Character - range.Start.Character));
        }

        // Multi-line (should not happen for tokens in our handler)
        var parts = new List<string>();
        parts.Add(lines[startLine].Substring((int)range.Start.Character));
        for (int l = startLine + 1; l < endLine; l++)
        {
            parts.Add(lines[l]);
        }
        parts.Add(lines[endLine].Substring(0, (int)range.End.Character));
        return string.Join("\n", parts);
    }

    [Fact]
    public async Task Handle_NoDocument_ReturnsEmpty()
    {
        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri1 },
            Position = new Position(0, 0),
            Context = new ReferenceContext()
        };

        var result = await this.handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_SimpleVariable_AllReferencesReturned()
    {
        var content1 = "$foo = 1\necho $foo\n$foo";
        var content2 = "echo $foo\n$bar = 2\n# $foo in comment should still count (plain text match)";
        var content3 = "$foobar = 3\n# ensure $foo does not match inside $foobar\n$foo";

        this.workspace.OpenDocument(uri1, content1, 1);
        this.workspace.OpenDocument(uri2, content2, 1);
        this.workspace.OpenDocument(uri3, content3, 1);

        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri1 },
            // Position over second occurrence in line 1: "echo $foo"
            Position = new Position(1, 7),
            Context = new ReferenceContext()
        };

        var result = await this.handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);

        var locs = result.ToList();

        // NOTE: The handler treats raw text occurrences; comments are not excluded.
        Assert.Equal(7, locs.Count);

        foreach (var loc in locs)
        {
            var docContent = this.workspace.GetDocument(loc.Uri)!.Content;
            var text = ExtractRangeText(docContent, loc.Range);
            Assert.Equal("$foo", text);
        }
    }

    [Fact]
    public async Task Handle_Variable_DoesNotMatchSubstringOfLongerVariable()
    {
        var content = "$foo = 1\n$foobar = 2\n$foo $foobar $foo";
        this.workspace.OpenDocument(uri1, content, 1);

        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri1 },
            Position = new Position(0, 2), // inside "$foo"
            Context = new ReferenceContext()
        };

        var result = await this.handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);

        var locs = result.ToList();
        // $foo occurrences: line0 col0-4, line2 col0-4, line2 second occurrence after space+variable ($foobar) -> col13? compute: line2: "$foo $foobar $foo"
        // indexes: 0..3 ($foo), 5..11 ($foobar), 13..16 ($foo) => 3 occurrences
        Assert.Equal(3, locs.Count);

        // Ensure none of the ranges correspond to "$foobar"
        foreach (var loc in locs)
        {
            var text = ExtractRangeText(content, loc.Range);
            Assert.Equal("$foo", text);
        }
    }

    [Fact]
    public async Task Handle_Identifier_AllReferencesReturned()
    {
        var content1 = "def greet []\n greet\nxgreet\n  greet";
        var content2 = "greet\n'greet in string literal maybe'\npre_greet";
        this.workspace.OpenDocument(uri1, content1, 1);
        this.workspace.OpenDocument(uri2, content2, 1);

        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri1 },
            Position = new Position(1, 2), // on "greet" (line 1)
            Context = new ReferenceContext()
        };

        var result = await this.handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);

        var occurrences = result.ToList();

        // Expected matches: content1 lines: def greet (identifier starting at 4) -> "greet" (line0)
        // line1 " greet" -> greet, line3 "  greet" -> greet. (xgreet should NOT match, substring inside token)
        // content2 line0 "greet" -> greet, line1 "'greet in ..." -> inside quotes still a plain token? It's surrounded by quotes and spaces; boundary logic will match 'greet' preceded by ' (not identifier part) and followed by space => counts.
        // "pre_greet" should NOT match because preceded by '_' boundary fail at start offset 'g' inside larger token or we won't index 'greet' substring due to left boundary check.
        // So total expected 5 occurrences.
        Assert.Equal(5, occurrences.Count);

        foreach (var loc in occurrences)
        {
            var docContent = this.workspace.GetDocument(loc.Uri)!.Content;
            var text = ExtractRangeText(docContent, loc.Range);
            Assert.Equal("greet", text);
        }
    }

    [Fact]
    public async Task Handle_PositionOnWhitespace_ReturnsEmpty()
    {
        var content = "$foo = 1\n\n$bar = 2";
        this.workspace.OpenDocument(uri1, content, 1);

        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri1 },
            Position = new Position(1, 0), // blank line
            Context = new ReferenceContext()
        };

        var result = await this.handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_SymbolAtEndOfToken_StillRecognized()
    {
        var content = "echo greet\n";
        this.workspace.OpenDocument(uri1, content, 1);

        // Position immediately after 'greet' (column 10) should still detect token (logic steps back one if preceding char is identifier part).
        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri1 },
            Position = new Position(0, 10),
            Context = new ReferenceContext()
        };

        var result = await this.handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        var locs = result.ToList();
        Assert.Single(locs);
        var text = ExtractRangeText(content, locs[0].Range);
        Assert.Equal("greet", text);
    }

    [Fact]
    public async Task Handle_SingleDollar_ReturnsEmpty()
    {
        var content = "$ \n$";
        this.workspace.OpenDocument(uri1, content, 1);

        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri1 },
            Position = new Position(0, 0),
            Context = new ReferenceContext()
        };

        var result = await this.handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetRegistrationOptions_HasDocumentSelector()
    {
        var opts = this.handler.GetRegistrationOptions(new ReferenceCapability(), new ClientCapabilities());
        Assert.NotNull(opts.DocumentSelector);
        Assert.Contains(opts.DocumentSelector, d => d.Pattern == "**/*.cosmos" || d.Pattern == "**/*.csh");
    }

    [Fact]
    public async Task Handle_Cancellation_ReturnsEmpty()
    {
        var content = "$foo $foo $foo";
        this.workspace.OpenDocument(uri1, content, 1);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri1 },
            Position = new Position(0, 2),
            Context = new ReferenceContext()
        };

        var result = await this.handler.Handle(request, cts.Token);
        Assert.NotNull(result);
        // Implementation returns empty container on cancellation
        Assert.Empty(result);
    }
}