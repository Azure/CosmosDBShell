// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System.Threading;
using System.Threading.Tasks;
using MediatR;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

internal class DidChangeWatchedFilesHandler : IDidChangeWatchedFilesHandler
{
    public DidChangeWatchedFilesRegistrationOptions GetRegistrationOptions() => new DidChangeWatchedFilesRegistrationOptions();

    Task<Unit> IRequestHandler<DidChangeWatchedFilesParams, Unit>.Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken) => Unit.Task;

    DidChangeWatchedFilesRegistrationOptions IRegistration<DidChangeWatchedFilesRegistrationOptions, DidChangeWatchedFilesCapability>.GetRegistrationOptions(DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities) => new DidChangeWatchedFilesRegistrationOptions();
}
