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

                // logging.AddEventSourceLogger();
            });
        var application = builder.Build();
        application.MapMcp();
        return application;
    }

    private static void ConfigureMcpServer(IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddSingleton<ToolOperations>();
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
                    Resources = new ResourcesCapability(),
                };

                mcpServerOptions.Handlers = new McpServerHandlers
                {
                    CallToolHandler = toolOperations.CallToolHandler,
                    ListToolsHandler = toolOperations.ListToolsHandler,
                };

                mcpServerOptions.ProtocolVersion = "2025-07-05";

                mcpServerOptions.ServerInstructions = LoadServerInstructions();
            });

        var mcpServerBuilder = services.AddMcpServer();
        mcpServerBuilder.WithResources<ResourceOperations>();
        mcpServerBuilder.WithHttpTransport();
    }

    private static string LoadServerInstructions()
    {
        var assembly = typeof(McpServer).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("serverinstructions.md", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new InvalidOperationException("Embedded resource 'serverinstructions.md' not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}