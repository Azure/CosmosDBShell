// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Lsp;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Lsp;
using Microsoft.Extensions.Logging.Abstractions;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using Xunit;

using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

public class CosmosShellFormattingHandlerTests
{
    private CosmosShellFormattingHandler CreateHandler(CosmosShellWorkspace workspace)
        => new(workspace, NullLogger<CosmosShellFormattingHandler>.Instance);

    private static FormattingOptions Spaces4()
        => new()
        {
            InsertSpaces = true,
            TabSize = 4
        };

    private static DocumentUri NewUri(string name) => DocumentUri.From($"file:///{name}.csh");

    [Fact]
    public async Task DocumentFormatting_FixesSimpleIfIndent()
    {
        var workspace = new CosmosShellWorkspace();
        var uri = NewUri("ifIndent");
        // Second line missing expected indent
        var content = "if true\necho \"hi\"\n";
        workspace.OpenDocument(uri, content, 1);

        var handler = CreateHandler(workspace);

        var result = await handler.Handle(new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Options = Spaces4()
        }, CancellationToken.None);

        Assert.NotNull(result);
        // Expect exactly one edit: add 4 spaces at start of line 1
        var edit = Assert.Single(result);
        Assert.NotNull(edit);
        Assert.Equal(1, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
        Assert.Equal(0, edit.Range.End.Character); // Replacing empty prefix
        Assert.Equal("    ", edit.NewText);
    }

    [Fact]
    public async Task DocumentFormatting_NoEditWhenAlreadyCorrect()
    {
        var workspace = new CosmosShellWorkspace();
        var uri = NewUri("alreadyOk");
        var content = "if true\n    echo \"hi\"\n";
        workspace.OpenDocument(uri, content, 1);

        var handler = CreateHandler(workspace);

        var result = await handler.Handle(new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Options = Spaces4()
        }, CancellationToken.None);

        Assert.Null(result); // No changes needed
    }

    [Fact]
    public async Task DocumentFormatting_RemovesIndentOnBlankLine()
    {
        var workspace = new CosmosShellWorkspace();
        var uri = NewUri("blankLine");
        // Line 1 has only spaces -> should be cleared
        var content = "echo \"hi\"\n    \n echo \"bye\"\n";
        workspace.OpenDocument(uri, content, 1);

        var handler = CreateHandler(workspace);

        var result = await handler.Handle(new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Options = Spaces4()
        }, CancellationToken.None);

        Assert.NotNull(result);
        // Should contain an edit clearing line 1 completely
        Assert.Contains(result, e =>
            e.Range.Start.Line == 1u &&
            e.Range.Start.Character == 0u &&
            e.Range.End.Character == 4u &&
            e.NewText == string.Empty);
    }

    [Fact]
    public async Task DocumentFormatting_BlockIndentAndClosingBrace()
    {
        var workspace = new CosmosShellWorkspace();
        var uri = NewUri("blockIndent");
        // Lines 2 & 3 mis-indented, closing brace correct
        var content = "if true\n{\necho \"x\"\n  echo \"y\"\n}\n";
        workspace.OpenDocument(uri, content, 1);

        var handler = CreateHandler(workspace);

        var result = await handler.Handle(new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Options = Spaces4()
        }, CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.ToList();
        // Expect two edits: line 2 (add 4 spaces), line 3 (replace 2 with 4 spaces)
        Assert.Equal(2, edits.Count);

        var line2 = edits.FirstOrDefault(e => e.Range.Start.Line == 2u);
        Assert.NotNull(line2);
        Assert.Equal(0, line2!.Range.Start.Character);
        Assert.Equal(0, line2.Range.End.Character);
        Assert.Equal("    ", line2.NewText);

        var line3 = edits.FirstOrDefault(e => e.Range.Start.Line == 3u);
        Assert.NotNull(line3);
        Assert.Equal(0, line3!.Range.Start.Character);
        Assert.Equal(2, line3.Range.End.Character);
        Assert.Equal("    ", line3.NewText);
    }

    [Fact]
    public async Task DocumentFormatting_FixesClosingBraceIndent()
    {
        var workspace = new CosmosShellWorkspace();
        var uri = NewUri("closingBrace");
        // Closing brace over-indented
        var content = "if true\n{\n    echo \"x\"\n    }\n";
        workspace.OpenDocument(uri, content, 1);

        var handler = CreateHandler(workspace);

        var result = await handler.Handle(new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Options = Spaces4()
        }, CancellationToken.None);

        Assert.NotNull(result);

        // Expect edit on line 3 replacing 4 spaces with empty prefix
        var edit = Assert.Single(result!, e => e.Range.Start.Line == 3u);
        Assert.Equal(0, edit.Range.Start.Character);
        Assert.Equal(4, edit.Range.End.Character);
        Assert.Equal(string.Empty, edit.NewText);
    }

    [Fact]
    public async Task RangeFormatting_OnlyTouchesSpecifiedLines()
    {
        var workspace = new CosmosShellWorkspace();
        var uri = NewUri("range");
        // Both lines mis-indented, but we format only line 1
        var content = "if true\necho \"a\"\necho \"b\"\n";
        workspace.OpenDocument(uri, content, 1);

        var handler = CreateHandler(workspace);

        var range = new Range(new Position(1, 0), new Position(1, 50));
        var result = await handler.Handle(new DocumentRangeFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Options = Spaces4(),
            Range = range
        }, CancellationToken.None);

        Assert.NotNull(result);
        var edits = result!.ToList();
        var single = Assert.Single(edits);
        Assert.Equal(1, single.Range.Start.Line);
        Assert.Equal("    ", single.NewText);

        // Ensure line 2 untouched (no edit for it)
        Assert.DoesNotContain(edits, e => e.Range.Start.Line == 2u);
    }

    [Fact]
    public async Task HeuristicIndent_AppliesWhenParseFails()
    {
        var workspace = new CosmosShellWorkspace();
        var uri = NewUri("heuristic");
        // Introduce a likely bad token to force parse issues while keeping predictable structure
        var content = "if true\nbad$token\n echo \"after\"\n";
        // Line 2 should indent one level (heuristic from preceding 'if true')
        workspace.OpenDocument(uri, content, 1);

        var handler = CreateHandler(workspace);

        var result = await handler.Handle(new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Options = Spaces4()
        }, CancellationToken.None);

        Assert.NotNull(result);
        // Expect an edit adding indent to line 1 (bad$token)
        Assert.Contains(result, e =>
            e.Range.Start.Line == 1 &&
            e.Range.Start.Character == 0 &&
            e.Range.End.Character == 0 &&
            e.NewText == "    ");
    }
}