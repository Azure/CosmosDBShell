// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Lsp;

using System.Linq;

using Azure.Data.Cosmos.Shell.Lsp;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using Xunit;

public class CosmosShellWorkspaceTests
{
    private readonly CosmosShellWorkspace workspace = new();
    private readonly DocumentUri uri = DocumentUri.From("file:///ws.csh");

    [Fact]
    public void OpenDocument_StoresAndParses()
    {
        this.workspace.OpenDocument(this.uri, "echo hello", 3);

        var doc = this.workspace.GetDocument(this.uri);
        Assert.NotNull(doc);
        Assert.Equal("echo hello", doc!.Content);
        Assert.Equal(3, doc.Version);
        Assert.NotNull(doc.LastParseResult);
        Assert.True(doc.LastParseResult!.Success);
    }

    [Fact]
    public void GetDocument_Unknown_ReturnsNull()
    {
        Assert.Null(this.workspace.GetDocument(DocumentUri.From("file:///missing.csh")));
    }

    [Fact]
    public void Documents_ReflectsOpenSet()
    {
        var other = DocumentUri.From("file:///other.csh");
        this.workspace.OpenDocument(this.uri, "echo a", 1);
        this.workspace.OpenDocument(other, "echo b", 1);

        Assert.Equal(2, this.workspace.Documents.Count());
    }

    [Fact]
    public void UpdateDocument_Existing_UpdatesContentAndVersion()
    {
        this.workspace.OpenDocument(this.uri, "echo a", 1);
        this.workspace.UpdateDocument(this.uri, "echo b", 2);

        var doc = this.workspace.GetDocument(this.uri);
        Assert.Equal("echo b", doc!.Content);
        Assert.Equal(2, doc.Version);
    }

    [Fact]
    public void UpdateDocument_Missing_OpensDocument()
    {
        this.workspace.UpdateDocument(this.uri, "echo new", 5);

        var doc = this.workspace.GetDocument(this.uri);
        Assert.NotNull(doc);
        Assert.Equal("echo new", doc!.Content);
        Assert.Equal(5, doc.Version);
    }

    [Fact]
    public void CloseDocument_RemovesIt()
    {
        this.workspace.OpenDocument(this.uri, "echo a", 1);
        this.workspace.CloseDocument(this.uri);

        Assert.Null(this.workspace.GetDocument(this.uri));
        Assert.Empty(this.workspace.Documents);
    }

    [Fact]
    public void GetParseResult_ReturnsDocumentResult()
    {
        this.workspace.OpenDocument(this.uri, "echo a", 1);

        var result = this.workspace.GetParseResult(this.uri);
        Assert.NotNull(result);
        Assert.True(result!.Success);
    }

    [Fact]
    public void GetParseResult_Unknown_ReturnsNull()
    {
        Assert.Null(this.workspace.GetParseResult(this.uri));
    }

    [Fact]
    public void GetWordAtPosition_Unknown_ReturnsNull()
    {
        Assert.Null(this.workspace.GetWordAtPosition(this.uri, new Position(0, 0)));
    }

    [Fact]
    public void GetWordAtPosition_ReturnsWord()
    {
        this.workspace.OpenDocument(this.uri, "echo $foo", 1);

        var word = this.workspace.GetWordAtPosition(this.uri, new Position(0, 6));
        Assert.Equal("$foo", word);
    }

    [Fact]
    public void GetWordAtPosition_LineOutOfRange_ReturnsNull()
    {
        this.workspace.OpenDocument(this.uri, "echo a", 1);

        Assert.Null(this.workspace.GetWordAtPosition(this.uri, new Position(10, 0)));
    }

    [Fact]
    public void GetWordAtPosition_CharOutOfRange_ReturnsNull()
    {
        this.workspace.OpenDocument(this.uri, "echo", 1);

        Assert.Null(this.workspace.GetWordAtPosition(this.uri, new Position(0, 20)));
    }

    [Fact]
    public void GetWordAtPosition_AtWordEnd_ReturnsPrecedingWord()
    {
        this.workspace.OpenDocument(this.uri, "a b", 1);

        Assert.Equal("a", this.workspace.GetWordAtPosition(this.uri, new Position(0, 1)));
    }

    [Fact]
    public void GetCompletionContext_Unknown_ReturnsEmpty()
    {
        var context = this.workspace.GetCompletionContext(this.uri, new Position(0, 0));
        Assert.Same(Azure.Data.Cosmos.Shell.Lsp.CompletionContext.Empty, context);
    }

    [Fact]
    public void GetCompletionContext_ReturnsContextWithText()
    {
        this.workspace.OpenDocument(this.uri, "echo hello", 1);

        var context = this.workspace.GetCompletionContext(this.uri, new Position(0, 4));
        Assert.Equal("echo", context.TextUpToPosition);
        Assert.NotNull(context.Document);
    }

    [Fact]
    public void OpenDocument_WithParseError_ProducesDiagnostics()
    {
        // Unterminated string should yield lexer errors -> diagnostics.
        this.workspace.OpenDocument(this.uri, "echo \"unterminated", 1);

        var doc = this.workspace.GetDocument(this.uri);
        Assert.NotNull(doc);
        Assert.NotEmpty(doc!.Diagnostics);
    }
}
