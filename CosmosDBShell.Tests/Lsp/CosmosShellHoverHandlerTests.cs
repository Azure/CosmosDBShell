// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Lsp;

using System;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp;
using Azure.Data.Cosmos.Shell.Lsp.Semantics;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Extensions.Logging.Abstractions;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using Xunit;

public class CosmosShellHoverHandlerTests : IDisposable
{
    private readonly CosmosShellWorkspace workspace;
    private readonly CosmosShellHoverHandler handler;
    private readonly DocumentUri testUri = DocumentUri.From("file:///test.cosmos");

    public CosmosShellHoverHandlerTests()
    {
        this.workspace = new CosmosShellWorkspace();
        this.handler = new CosmosShellHoverHandler(
            this.workspace,
            NullLogger<CosmosShellHoverHandler>.Instance);

        // Initialize ShellInterpreter instance for command lookups
        _ = ShellInterpreter.Instance;
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task Handle_NoDocument_ReturnsNull()
    {
        // Arrange
        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 0)
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CommandHover_ReturnsCommandMarkdown()
    {
        // Arrange
        var content = "connect -endpoint:https://example.com";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 3) // Position on "connect"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Contents);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Equal(MarkupKind.Markdown, markup.Kind);
        Assert.Contains("connect", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_UnknownCommand_ReturnsUnknownMessage()
    {
        // Arrange
        var content = "unknowncmd arg1 arg2";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 5) // Position on "unknowncmd"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("unknowncmd", markup.Value);
    }

    [Fact]
    public async Task Handle_VariableHover_ReturnsVariableInfo()
    {
        // Arrange
        var content = "$myVar = 42\necho $myVar";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(1, 6) // Position on "$myVar" in echo
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("myVar", markup.Value);
    }

    [Fact]
    public async Task Handle_IfStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "if $x > 10\n    echo \"big\"";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 1) // Position on "if"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("if", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WhileStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "while $count < 5\n    $count = $count + 1";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 2) // Position on "while"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("while", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_ForStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "for $item in [1,2,3]\n    echo $item";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 1) // Position on "for"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("for", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_DefStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "def greet [name]\n    echo \"Hello $name\"";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 1) // Position on "def"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("def", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_BreakStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "while true\n    break";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(1, 5) // Position on "break"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("break", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_ContinueStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "for $i in [1,2,3]\n    continue";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(1, 5) // Position on "continue"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("continue", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_ReturnStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "def test\n    return 42";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(1, 5) // Position on "return"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("return", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_ElseKeyword_ReturnsStatementHelp()
    {
        // Arrange
        var content = "if $x > 10\n    echo \"big\"\nelse\n    echo \"small\"";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(2, 1) // Position on "else"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // p
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        // "else" uses "if" help key
        Assert.Contains("if", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_DoWhileStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "do\n    echo \"test\"\nwhile $x < 5";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 1) // Position on "do"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("do", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_LoopStatement_ReturnsStatementHelp()
    {
        // Arrange
        var content = "loop\n    echo \"infinite\"";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 2) // Position on "loop"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("loop", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_PositionOutsideToken_ReturnsNull()
    {
        // Arrange
        var content = "echo test";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 5) // Position on space between "echo" and "test"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        // Should return null or no meaningful hover for whitespace
        if (result != null)
        {
            var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
            Assert.DoesNotContain("echo", markup.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Handle_CommandWithOptions_ShowsOptionsInHover()
    {
        // Arrange
        var content = "query \"SELECT * FROM c\" -max:10";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(0, 2) // Position on "query"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("query", markup.Value, StringComparison.OrdinalIgnoreCase);
        // Should contain option information
        Assert.Contains("-max", markup.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_FunctionCall_ReturnsFunctionInfo()
    {
        // Arrange
        var content = "def myFunc []\n    echo \"test\"\n\nmyFunc";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(3, 2) // Position on "myFunc" call
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("myFunc", markup.Value);
    }

    [Fact]
    public void GetRegistrationOptions_ReturnsCorrectDocumentSelector()
    {
        // Arrange
        var capability = new HoverCapability();
        var clientCapabilities = new ClientCapabilities();

        // Act
        var options = this.handler.GetRegistrationOptions(capability, clientCapabilities);

        // Assert
        Assert.NotNull(options);
        Assert.NotNull(options.DocumentSelector);
        Assert.Contains(options.DocumentSelector, filter =>
            filter.Pattern == "**/*.cosmos" || filter.Pattern == "**/*.csh");
    }

    [Fact]
    public async Task Handle_NestedStatements_FindsCorrectToken()
    {
        // Arrange
        var content = "if $x > 0\n    while $y < 10\n        echo $y";
        this.workspace.OpenDocument(testUri, content, 1);
        var doc = this.workspace.GetDocument(testUri);
        doc!.Parse();

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = testUri },
            Position = new Position(1, 6) // Position on nested "while"
        };

        // Act
        var result = await this.handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var markup = Assert.IsType<MarkupContent>(result.Contents.MarkupContent);
        Assert.Contains("while", markup.Value, StringComparison.OrdinalIgnoreCase);
    }
}