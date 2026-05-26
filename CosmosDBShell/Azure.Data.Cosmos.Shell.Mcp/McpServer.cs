// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System;
using System.Net;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using static Program;

/// <summary>
/// MCP Server implementation for running shell commands via HTTP.
/// Provides a local HTTP endpoint that accepts command execution requests.
/// SECURITY NOTE: This server is designed to run locally only and should not be exposed to external networks.
/// </summary>
internal class McpServer
{
    public static IHost CreateHost(CosmosShellOptions serverArguments)
    {
        var builder = WebApplication.CreateBuilder([]);
        ConfigureMcpServer(builder.Services);
        builder.WebHost
            .ConfigureKestrel(server =>
            {
                // Bind explicitly to IPv4 loopback only for maximum security
                // This prevents any external network access, even with disabled firewalls
                server.Listen(IPAddress.Loopback, serverArguments.McpPort!.Value);
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Error);
            });
        var application = builder.Build();
        application.UseOriginValidation();
        application.MapMcp();
        return application;
    }

    private static void ConfigureMcpServer(IServiceCollection services)
    {
        services.AddSingleton<ToolOperations>();
        services.AddSingleton<ResourceSubscriptionManager>();
        services.AddOptions<McpServerOptions>()
            .Configure<ToolOperations, ResourceSubscriptionManager>((mcpServerOptions, toolOperations, subscriptions) =>
            {
                var entryAssembly = Assembly.GetEntryAssembly();
                var assemblyName = entryAssembly?.GetName();
                var serverName = entryAssembly?.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "Cosmos Shell MCP Server";

                mcpServerOptions.ServerInfo = new Implementation
                {
                    Name = serverName,
                    Version = assemblyName?.Version?.ToString() ?? "1.0.0-beta",
                };

                mcpServerOptions.Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability(),
                    Resources = new ResourcesCapability
                    {
                        Subscribe = true,
                    },
                    Completions = new CompletionsCapability(),
                    Prompts = new PromptsCapability(),
                };

                mcpServerOptions.Handlers = new McpServerHandlers
                {
                    CallToolHandler = toolOperations.CallToolHandler,
                    ListToolsHandler = toolOperations.ListToolsHandler,
                    SubscribeToResourcesHandler = (context, _) =>
                    {
                        var uri = context.Params?.Uri;
                        if (!string.IsNullOrWhiteSpace(uri))
                        {
                            subscriptions.Subscribe(uri, context.Server);
                        }

                        return new ValueTask<EmptyResult>(new EmptyResult());
                    },
                    UnsubscribeFromResourcesHandler = (context, _) =>
                    {
                        var uri = context.Params?.Uri;
                        if (!string.IsNullOrWhiteSpace(uri))
                        {
                            subscriptions.Unsubscribe(uri, context.Server);
                        }

                        return new ValueTask<EmptyResult>(new EmptyResult());
                    },
                };

                mcpServerOptions.ProtocolVersion = "2025-07-05";

                mcpServerOptions.ServerInstructions = LoadServerInstructions();
            });

        var mcpServerBuilder = services.AddMcpServer();
        mcpServerBuilder.WithResources<ResourceOperations>();
        mcpServerBuilder.WithPrompts<PromptOperations>();
        mcpServerBuilder.WithListResourceTemplatesHandler(ResourceCompletionOperations.ListResourceTemplatesAsync);
        mcpServerBuilder.WithCompleteHandler(ResourceCompletionOperations.CompleteAsync);
        mcpServerBuilder.WithHttpTransport();
    }

    private static string LoadServerInstructions()
    {
        return EmbeddedResourceLoader.Load(typeof(McpServer).Assembly, "serverinstructions.md");
    }
}