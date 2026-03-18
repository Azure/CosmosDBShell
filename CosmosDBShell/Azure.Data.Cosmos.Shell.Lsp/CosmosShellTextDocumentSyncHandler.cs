// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

internal class CosmosShellTextDocumentSyncHandler : ITextDocumentSyncHandler
{
    private readonly CosmosShellWorkspace workspace;
    private readonly ILanguageServerFacade languageServer;
    private readonly ILogger<CosmosShellTextDocumentSyncHandler> logger;

    public CosmosShellTextDocumentSyncHandler(
        CosmosShellWorkspace workspace,
        ILanguageServerFacade languageServer,
        ILogger<CosmosShellTextDocumentSyncHandler> logger)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        this.languageServer = languageServer ?? throw new ArgumentNullException(nameof(languageServer));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TextDocumentChangeRegistrationOptions GetRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentChangeRegistrationOptions
        {
            DocumentSelector = LspServer.DocumentSelector,
            SyncKind = TextDocumentSyncKind.Full,
        };
    }

    public TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "cosmosshell");
    }

    public Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        this.logger.LogInformation("Opening document: {Uri}", request.TextDocument.Uri);

        this.workspace.OpenDocument(
            request.TextDocument.Uri,
            request.TextDocument.Text,
            request.TextDocument.Version ?? 0);

        // Publish initial diagnostics
        this.PublishDiagnostics(request.TextDocument.Uri);

        return Unit.Task;
    }

    public Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        this.logger.LogDebug("Changing document: {Uri}", request.TextDocument.Uri);

        // Since we're using Full sync, we get the complete document content
        var change = request.ContentChanges.FirstOrDefault();
        if (change != null)
        {
            this.workspace.UpdateDocument(
                request.TextDocument.Uri,
                change.Text,
                request.TextDocument.Version ?? 0);

            // Publish updated diagnostics
            this.PublishDiagnostics(request.TextDocument.Uri);
        }

        return Unit.Task;
    }

    public Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        this.logger.LogInformation("Closing document: {Uri}", request.TextDocument.Uri);

        this.workspace.CloseDocument(request.TextDocument.Uri);

        // Clear diagnostics for closed document
        this.languageServer.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>(),
        });

        return Unit.Task;
    }

    public Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        this.logger.LogDebug("Saved document: {Uri}", request.TextDocument.Uri);

        // Optionally re-validate on save
        if (request.Text != null)
        {
            this.workspace.UpdateDocument(
                request.TextDocument.Uri,
                request.Text,
                this.workspace.GetDocument(request.TextDocument.Uri)?.Version ?? 0);

            this.PublishDiagnostics(request.TextDocument.Uri);
        }

        return Unit.Task;
    }

    TextDocumentOpenRegistrationOptions IRegistration<TextDocumentOpenRegistrationOptions, TextSynchronizationCapability>
        .GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentOpenRegistrationOptions
        {
            DocumentSelector = LspServer.DocumentSelector,
        };
    }

    TextDocumentCloseRegistrationOptions IRegistration<TextDocumentCloseRegistrationOptions, TextSynchronizationCapability>
        .GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentCloseRegistrationOptions
        {
            DocumentSelector = LspServer.DocumentSelector,
        };
    }

    TextDocumentSaveRegistrationOptions IRegistration<TextDocumentSaveRegistrationOptions, TextSynchronizationCapability>
        .GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSaveRegistrationOptions
        {
            DocumentSelector = LspServer.DocumentSelector,
            IncludeText = true,
        };
    }

    private void PublishDiagnostics(DocumentUri uri)
    {
        var document = this.workspace.GetDocument(uri);
        if (document != null)
        {
            this.languageServer.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = uri,
                Version = document.Version,
                Diagnostics = new Container<Diagnostic>(document.Diagnostics),
            });
        }
    }
}