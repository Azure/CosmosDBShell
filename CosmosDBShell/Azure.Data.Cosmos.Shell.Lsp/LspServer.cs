// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;

internal class LspServer
{
    public static readonly TextDocumentSelector DocumentSelector = new(
        new TextDocumentFilter { Pattern = "**/*.cosmos" },
        new TextDocumentFilter { Pattern = "**/*.csh" });

    public static async Task<LanguageServer> CreateLanguageServerAsync()
    {
        var server = await LanguageServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.SetMinimumLevel(LogLevel.Warning);
                })
                .WithHandler<DidChangeWatchedFilesHandler>()
                .WithHandler<CosmosShellTextDocumentSyncHandler>()
                .WithHandler<CosmosShellSemanticTokensHandler>()
                .WithHandler<CosmosShellHoverHandler>()
                .WithHandler<CosmosShellReferencesHandler>()
                .WithHandler<FoldingRangeHandler>()
                .WithHandler<CosmosShellCompletionHandler>()
                .WithHandler<CosmosShellFormattingHandler>()
                .OnInitialize((server, request, token) =>
                {
                    return Task.CompletedTask;
                })
                .WithServices(services =>
                {
                    services.AddSingleton<CosmosShellWorkspace>();
                }));
        return server;
    }
}
