// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Util;
using CommandLine;
using CommandLine.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using Spectre.Console;

internal class Program
{
    private const int DefaultMcpPort = 6128;

    public static async Task Main(string[] args)
    {
        // Handle LSP mode early, before any other code can write to stdout.
        // The LSP protocol requires exclusive access to stdin/stdout.
        if (args.Contains("--lsp") || args.Contains("--stdio"))
        {
            var server = await LspServer.CreateLanguageServerAsync();
            await server.WaitForExit;
            return;
        }

        IHost? host = null;
        try
        {
            args = NormalizeArguments(args);
            SentenceBuilder.Factory = () => new LocalizableSentenceBuilder();
            var parser = Parser.Default.ParseArguments<CosmosShellOptions>(args);

            // Handle parse errors
            parser.WithNotParsed(errors =>
            {
                Environment.ExitCode = 1;
                return;
            });

            _ = await parser.WithParsedAsync(async o =>
            {
                if (o.StartLspServer)
                {
                    // Already handled above, but keep for completeness
                    var server = await LspServer.CreateLanguageServerAsync();
                    await server.WaitForExit;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(o.ExecuteAndQuit) && !string.IsNullOrWhiteSpace(o.ExecuteAndContinue))
                {
                    Environment.ExitCode = 1;
                    ShellInterpreter.WriteLine(MessageService.GetString("error-mutually-exclusive-options"));
                    return;
                }

                var executeAndQuitCommand = string.IsNullOrWhiteSpace(o.ExecuteAndQuit) ? null : o.ExecuteAndQuit;
                var executeAndContinueCommand = string.IsNullOrWhiteSpace(o.ExecuteAndContinue) ? null : o.ExecuteAndContinue;
                var explicitCommand = executeAndContinueCommand ?? executeAndQuitCommand;

                if (o.ClearHistory)
                {
                    if (File.Exists(ShellInterpreter.Instance.HistoryFile))
                    {
                        File.Delete(ShellInterpreter.Instance.HistoryFile);
                    }

                    ShellInterpreter.WriteLine(MessageService.GetString("shell-hisory_file_deleted"));
                    return;
                }

                AnsiConsole.Profile.Capabilities.ColorSystem = o.ColorSystem switch
                {
                    1 => ColorSystem.Standard,
                    2 => ColorSystem.TrueColor,
                    _ => ColorSystem.NoColors,
                };
                ShellInterpreter.Instance.Options = o;

                if (o.ConnectionString != null)
                {
                    try
                    {
                        await ShellInterpreter.Instance.ConnectAsync(
                            o.ConnectionString,
                            o.ConnectHint,
                            o.ConnectionMode,
                            tenantId: o.ConnectTenant,
                            authorityHost: o.ConnectAuthorityHost,
                            managedIdentityClientId: o.ConnectManagedIdentity,
                            useVSCodeCredential: o.ConnectVSCodeCredential);
                    }
                    catch (Exception ex)
                    {
                        Environment.ExitCode = 1;
                        if (ConnectCommand.TryGetPrincipalIdFromRbacException(ex, out var id, out var permission))
                        {
                            ConnectCommand.AskForRBacPermissions(id ?? string.Empty, permission ?? string.Empty);
                            return;
                        }

                        ShellInterpreter.WriteLine(ex.Message);
                        return;
                    }
                }

                // Start MCP server if requested
                Task? hostTask = null;

                if (o.McpPort is int mcpPort)
                {
                    if (mcpPort <= 0)
                    {
                        AnsiConsole.WriteLine(MessageService.GetString("mcp-error-invalid-port"));
                        Environment.ExitCode = 1;
                        return;
                    }

                    try
                    {
                        host = McpServer.CreateHost(o);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.WriteLine(MessageService.GetArgsString("mcp-error-creating-server", "message", Markup.Escape(ex.Message)));
                        Environment.ExitCode = 1;
                        return;
                    }

                    if (host != null)
                    {
                        ShellInterpreter.Instance.McpPort = mcpPort;
                        hostTask = Task.Run(async () =>
                        {
                            try
                            {
                                var token = CancellationToken.None;
                                await host.StartAsync(token);
                                await host.WaitForShutdownAsync(token);
                            }
                            catch (Exception ex)
                            {
                                AnsiConsole.WriteLine(MessageService.GetArgsString("mcp-error-server-failed-start", "message", Markup.Escape(ex.Message)));
                                Environment.ExitCode = 1;
                            }
                        });
                    }
                }

                if (Console.IsInputRedirected)
                {
                    // Read entire stdin (script)
                    var script = await Console.In.ReadToEndAsync() + Environment.NewLine;
                    if (!string.IsNullOrWhiteSpace(script))
                    {
                        var state = await ShellInterpreter.Instance.ExecuteCommandAsync(script, default);
                        if (state.IsError)
                        {
                            Environment.ExitCode = 1;
                            if (executeAndContinueCommand is null)
                            {
                                return;
                            }
                        }

                        // If user only wants to execute piped script then quit (unless -k / ExecuteAndContinue)
                        if (executeAndContinueCommand is null && executeAndQuitCommand is null)
                        {
                            // Stop host gracefully before returning
                            if (host != null)
                            {
                                await host.StopAsync();
                            }

                            return;
                        }
                    }
                }

                if (explicitCommand is not null)
                {
                    var state = await ShellInterpreter.Instance.ExecuteCommandAsync(explicitCommand, default);
                    if (state.IsError)
                    {
                        Environment.ExitCode = 1;
                        if (executeAndContinueCommand is null)
                        {
                            // Stop host gracefully before returning
                            if (host != null)
                            {
                                await host.StopAsync();
                            }

                            // Wait for the host task to complete
                            if (hostTask != null)
                            {
                                await hostTask;
                            }

                            return;
                        }
                    }

                    if (executeAndContinueCommand is not null)
                    {
                        await ShellInterpreter.Instance.RunAsync();
                    }
                }
                else
                {
                    await ShellInterpreter.Instance.RunAsync();
                }

                // Stop the host gracefully before the task completes
                if (host != null)
                {
                    await host.StopAsync();
                }

                // Wait for the host task to complete
                if (hostTask != null)
                {
                    await hostTask;
                }
            });
        }
        finally
        {
            ShellInterpreter.Instance.Dispose();
            host?.Dispose();
        }
    }

    private static string[] NormalizeArguments(string[] args)
    {
        var normalizedArguments = new List<string>(args.Length);

        for (int index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--mcp")
            {
                if (index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    normalizedArguments.Add(argument);
                    normalizedArguments.Add(args[++index]);
                    continue;
                }

                normalizedArguments.Add($"--mcp={DefaultMcpPort}");
                continue;
            }

            normalizedArguments.Add(argument);
        }

        return [.. normalizedArguments];
    }

    public class CosmosShellOptions
    {
        [Option("cs", Required = false, HelpText = "ColorSystem", ResourceType = typeof(LocalizableSentenceBuilder))]
        public int ColorSystem { get; set; } = 2;

        [Option('c', Required = false, HelpText = "ExecuteAndQuit", ResourceType = typeof(LocalizableSentenceBuilder))]
        public string? ExecuteAndQuit { get; set; }

        [Option('k', Required = false, HelpText = "ExecuteAndContinue", ResourceType = typeof(LocalizableSentenceBuilder))]
        public string? ExecuteAndContinue { get; set; }

        [Option("clearhistory", Required = false, HelpText = "ClearHistory", ResourceType = typeof(LocalizableSentenceBuilder))]
        public bool ClearHistory { get; set; }

        [Option("connect", Required = false, HelpText = "ConnectionString", ResourceType = typeof(LocalizableSentenceBuilder))]
        public string? ConnectionString { get; set; }

        [Option("connect-mode", Required = false, HelpText = "ConnectionMode", ResourceType = typeof(LocalizableSentenceBuilder))]
        public ConnectionMode? ConnectionMode { get; set; }

        [Option("connect-tenant", Required = false, HelpText = "ConnectTenant", ResourceType = typeof(LocalizableSentenceBuilder))]
        public string? ConnectTenant { get; set; }

        [Option("connect-hint", Required = false, HelpText = "ConnectHint", ResourceType = typeof(LocalizableSentenceBuilder))]
        public string? ConnectHint { get; set; }

        [Option("connect-authority-host", Required = false, HelpText = "ConnectAuthorityHost", ResourceType = typeof(LocalizableSentenceBuilder))]
        public string? ConnectAuthorityHost { get; set; }

        [Option("connect-managed-identity", Required = false, HelpText = "ConnectManagedIdentity", ResourceType = typeof(LocalizableSentenceBuilder))]
        public string? ConnectManagedIdentity { get; set; }

        [Option("connect-vscode-credential", Required = false, HelpText = "ConnectVSCodeCredential", ResourceType = typeof(LocalizableSentenceBuilder), Hidden = true)]
        public bool ConnectVSCodeCredential { get; set; }

        [Option("mcp", Required = false, HelpText = "McpPort", ResourceType = typeof(LocalizableSentenceBuilder))]
        public int? McpPort { get; set; }

        [Option("lsp", Required = false, HelpText = "EnableLspServer", ResourceType = typeof(LocalizableSentenceBuilder))]
        public bool StartLspServer { get; set; }

        [Option("stdio", Required = false, HelpText = "EnableLspServer", ResourceType = typeof(LocalizableSentenceBuilder), Hidden = true)]
        public bool LspStdio { get; set; }
    }
}