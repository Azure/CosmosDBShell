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

using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using static Program;

/// <summary>
/// MCP Server implementation for running shell commands via HTTP or stdio.
/// SECURITY NOTE: This server is designed to run locally only and should not be exposed to external networks.
/// </summary>
internal class McpServer
{
    internal static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(10);

    public static IHost CreateHost(CosmosShellOptions serverArguments)
    {
        var builder = WebApplication.CreateBuilder([]);
        ConfigureMcpServer(builder.Services).WithHttpTransport(ConfigureHttpTransport);
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

    public static IHost CreateStdioHost(Stream input, Stream output)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            DisableDefaults = true,
        });
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var protocolInput = new StdioInputStream(input);
        ConfigureMcpServer(builder.Services, stdio: true).WithStreamServerTransport(protocolInput, output);
        var host = builder.Build();
        protocolInput.HostLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        return host;
    }

    private static IMcpServerBuilder ConfigureMcpServer(IServiceCollection services, bool stdio = false)
    {
        services.AddSingleton<ToolOperations>();
        services.AddSingleton(services => new LocationResourceSubscriptions(
            services.GetRequiredService<ILogger<LocationResourceSubscriptions>>(),
            services.GetRequiredService<IHostApplicationLifetime>(),
            stdio));
        services.AddHostedService(services => services.GetRequiredService<LocationResourceSubscriptions>());
        services.AddOptions<McpServerOptions>()
            .Configure<ToolOperations, LocationResourceSubscriptions>((mcpServerOptions, toolOperations, locationSubscriptions) =>
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
                    Resources = new ResourcesCapability { Subscribe = true, ListChanged = false },
                };

                mcpServerOptions.Handlers = new McpServerHandlers
                {
                    CallToolHandler = toolOperations.CallToolHandler,
                    ListToolsHandler = toolOperations.ListToolsHandler,
                    ListResourcesHandler = ResourceOperations.ListResourcesAsync,
                    ReadResourceHandler = ResourceOperations.ReadResourceAsync,
                    SubscribeToResourcesHandler = toolOperations.SubscribeToResourcesHandler,
                    UnsubscribeFromResourcesHandler = toolOperations.UnsubscribeFromResourcesHandler,
                    SubscriptionsListenHandler = locationSubscriptions.ListenAsync,
                };

                mcpServerOptions.ServerInstructions = LoadServerInstructions();
            });

        // SDK resource collections advertise list changes even when configured otherwise.
        return services.AddMcpServer();
    }

    internal static void ConfigureHttpTransport(HttpServerTransportOptions options)
    {
        // 2026-07-28 clients are served statelessly (confirmation via MRTR, updates via subscriptions/listen).
        // Clients that use the initialize handshake still get a session for elicitation and resources/subscribe.
        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;

        // Sessions with an open GET stream never go idle. Once a client disconnects without DELETE,
        // the session is disposed after this timeout, which also ends its location subscription.
#pragma warning disable MCP9006 // Stateful Streamable HTTP options are required for session-bound features.
        options.IdleTimeout = SessionIdleTimeout;
#pragma warning restore MCP9006

#pragma warning disable MCPEXP002 // RunSessionHandler is the only hook that observes the session lifetime.
        options.RunSessionHandler = (httpContext, server, cancellationToken) =>
            httpContext.RequestServices.GetRequiredService<LocationResourceSubscriptions>().RunSessionAsync(server, cancellationToken);
#pragma warning restore MCPEXP002
    }

    private static string LoadServerInstructions()
    {
        return EmbeddedResourceLoader.Load(typeof(McpServer).Assembly, "serverinstructions.md");
    }
}