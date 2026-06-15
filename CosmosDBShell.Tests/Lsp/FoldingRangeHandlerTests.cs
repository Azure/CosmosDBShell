// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Lsp;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Lsp;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using Xunit;

public class FoldingRangeHandlerTests
{
    private readonly CosmosShellWorkspace workspace = new();
    private readonly FoldingRangeHandler handler;
    private readonly DocumentUri uri = DocumentUri.From("file:///fold.csh");

    public FoldingRangeHandlerTests()
    {
        this.handler = new FoldingRangeHandler(this.workspace);
    }

    private Task<Container<FoldingRange>?> HandleAsync()
    {
        return this.handler.Handle(
            new FoldingRangeRequestParam { TextDocument = new TextDocumentIdentifier { Uri = this.uri } },
            CancellationToken.None);
    }

    [Fact]
    public async Task Handle_NoDocument_ReturnsNull()
    {
        var result = await this.HandleAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NoBraces_ReturnsEmpty()
    {
        this.workspace.OpenDocument(this.uri, "echo hello\necho world", 1);

        var result = await this.HandleAsync();

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task Handle_SingleLineBraces_NotFolded()
    {
        this.workspace.OpenDocument(this.uri, "if true { echo x }", 1);

        var result = await this.HandleAsync();

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task Handle_MultiLineBraces_ProducesRange()
    {
        this.workspace.OpenDocument(this.uri, "if true {\n  echo x\n}", 1);

        var result = await this.HandleAsync();

        Assert.NotNull(result);
        var range = Assert.Single(result!);
        Assert.Equal(0, range.StartLine);
        Assert.Equal(2, range.EndLine);
        Assert.Equal(FoldingRangeKind.Region, range.Kind);
    }

    [Fact]
    public async Task Handle_NestedBraces_ProducesSortedRanges()
    {
        this.workspace.OpenDocument(this.uri, "if a {\n  if b {\n    echo x\n  }\n}", 1);

        var result = await this.HandleAsync();

        Assert.NotNull(result);
        var ranges = result!.ToList();
        Assert.Equal(2, ranges.Count);

        // Sorted by start line ascending.
        Assert.Equal(0, ranges[0].StartLine);
        Assert.Equal(1, ranges[1].StartLine);
    }

    [Fact]
    public async Task Handle_UnbalancedCloseBrace_Ignored()
    {
        this.workspace.OpenDocument(this.uri, "echo x\n}\n}", 1);

        var result = await this.HandleAsync();

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void GetRegistrationOptions_TargetsCosmosShell()
    {
        var options = this.handler.GetRegistrationOptions();
        Assert.NotNull(options.DocumentSelector);
    }
}
