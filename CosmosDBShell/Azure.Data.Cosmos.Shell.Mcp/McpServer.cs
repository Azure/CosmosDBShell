// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System;
using System.Net;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    private const string McpSessionIdHeaderName = "Mcp-Session-Id";

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
        application.Use(RemoveLocationSubscriptionsWhenNotificationStreamEndsAsync);
        application.MapMcp();
        return application;
    }

    // The SDK accepts one notification stream (GET) per session and swallows write failures on it,
    // so once that request ends the session can no longer receive location updates.
    private static async Task RemoveLocationSubscriptionsWhenNotificationStreamEndsAsync(HttpContext context, RequestDelegate next)
    {
        var sessionId = HttpMethods.IsGet(context.Request.Method)
            ? context.Request.Headers[McpSessionIdHeaderName].ToString()
            : string.Empty;
        try
        {
            await next(context);
        }
        finally
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                context.RequestServices.GetRequiredService<LocationResourceSubscriptions>().RemoveSession(sessionId);
            }
        }
    }

    private static void ConfigureMcpServer(IServiceCollection services)
    {
        services.AddSingleton<ToolOperations>();
        services.AddSingleton<LocationResourceSubscriptions>();
        services.AddHostedService(services => services.GetRequiredService<LocationResourceSubscriptions>());
        services.AddOptions<McpServerOptions>()
            .Configure<ToolOperations>((mcpServerOptions, toolOperations) =>
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
                    Resources = new ResourcesCapability { Subscribe = true },
                };

                mcpServerOptions.Handlers = new McpServerHandlers
                {
                    CallToolHandler = toolOperations.CallToolHandler,
                    ListToolsHandler = toolOperations.ListToolsHandler,
                    SubscribeToResourcesHandler = toolOperations.SubscribeToResourcesHandler,
                    UnsubscribeFromResourcesHandler = toolOperations.UnsubscribeFromResourcesHandler,
                };

                mcpServerOptions.ServerInstructions = LoadServerInstructions();
            });

        var mcpServerBuilder = services.AddMcpServer();
        mcpServerBuilder.WithResources<ResourceOperations>();
        mcpServerBuilder.WithHttpTransport();
    }

    private static string LoadServerInstructions()
    {
        return EmbeddedResourceLoader.Load(typeof(McpServer).Assembly, "serverinstructions.md");
    }
}