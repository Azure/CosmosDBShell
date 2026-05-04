// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

internal class Program
{
    private const int DefaultMcpPort = 6128;

    public static async Task Main(string[] args)
    {
        // Normalize argv first so that any --lsp/--stdio token that is part
        // of a -c/-k command tail is absorbed as command text and does not
        // accidentally trigger LSP mode.
        args = NormalizeArguments(args);

        // Handle LSP mode early, before any other code can write to stdout.
        // The LSP protocol requires exclusive access to stdin/stdout. Only
        // inspect the prefix before -c / -k so that a command tail of literally
        // "--lsp" or "--stdio" is forwarded to the shell command rather than
        // accidentally starting the LSP server.
        var preCommandLsp = TakePreCommandArgs(args);
        if (preCommandLsp.Any(a => a is "--lsp" or "--stdio"))
        {
            var server = await LspServer.CreateLanguageServerAsync();
            await server.WaitForExit;
            return;
        }

        IHost? host = null;
        try
        {
            // --help / --version handled manually so we can render our own
            // localized usage and version heading. Only inspect the prefix
            // before -c / -k so that a command tail of literally "--help" or
            // "--version" (e.g. `-c --help`) is forwarded to the shell command
            // instead of intercepted here.
            var preCommandArgs = TakePreCommandArgs(args);
            if (preCommandArgs.Any(a => a is "--help" or "-h" or "-?" or "/?" or "/h"))
            {
                ShellInterpreter.WriteLine(BuildHelpText());
                return;
            }

            if (preCommandArgs.Any(a => a is "--version"))
            {
                WriteVersionHeading();
                return;
            }

            var (rootCommand, optionMap) = BuildRootCommand();
            var configuration = new System.CommandLine.CommandLineConfiguration(
                rootCommand,
                resources: new LocalizedCliResources());
            var parser = new System.CommandLine.Parsing.Parser(configuration);
            var parseResult = parser.Parse(args);

            if (parseResult.Errors.Count > 0)
            {
                foreach (var error in parseResult.Errors)
                {
                    ShellInterpreter.WriteLine(error.Message);
                }

                ShellInterpreter.WriteLine(BuildHelpText());
                Environment.ExitCode = 1;
                return;
            }

            var o = new CosmosShellOptions
            {
                ColorSystem = parseResult.GetValueForOption(optionMap.ColorSystem),
                ExecuteAndQuit = parseResult.GetValueForOption(optionMap.ExecuteAndQuit),
                ExecuteAndContinue = parseResult.GetValueForOption(optionMap.ExecuteAndContinue),
                ClearHistory = parseResult.GetValueForOption(optionMap.ClearHistory),
                ConnectionString = parseResult.GetValueForOption(optionMap.ConnectionString),
                ConnectionMode = parseResult.GetValueForOption(optionMap.ConnectionMode),
                ConnectTenant = parseResult.GetValueForOption(optionMap.ConnectTenant),
                ConnectHint = parseResult.GetValueForOption(optionMap.ConnectHint),
                ConnectAuthorityHost = parseResult.GetValueForOption(optionMap.ConnectAuthorityHost),
                ConnectManagedIdentity = parseResult.GetValueForOption(optionMap.ConnectManagedIdentity),
                ConnectVSCodeCredential = parseResult.GetValueForOption(optionMap.ConnectVSCodeCredential),
                StartLspServer = parseResult.GetValueForOption(optionMap.StartLspServer),
                LspStdio = parseResult.GetValueForOption(optionMap.LspStdio),
                Verbose = parseResult.GetValueForOption(optionMap.Verbose),
            };

            // --mcp supports an optional value: when the option is present without
            // an integer, fall back to the default port.
            var mcpResult = parseResult.FindResultFor(optionMap.McpPort);
            if (mcpResult is not null)
            {
                var mcpValue = parseResult.GetValueForOption(optionMap.McpPort);
                o.McpPort = mcpValue ?? DefaultMcpPort;
            }

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
                using var connectTokenSource = ShellInterpreter.UserCancellationTokenSource;
                var connectToken = connectTokenSource.Token;
                try
                {
                    await ShellInterpreter.Instance.ConnectAsync(
                        o.ConnectionString,
                        o.ConnectHint,
                        o.ConnectionMode,
                        tenantId: o.ConnectTenant,
                        authorityHost: o.ConnectAuthorityHost,
                        managedIdentityClientId: o.ConnectManagedIdentity,
                        useVSCodeCredential: o.ConnectVSCodeCredential,
                        token: connectToken);
                }
                catch (OperationCanceledException) when (connectToken.IsCancellationRequested)
                {
                    return;
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
        }
        finally
        {
            ShellInterpreter.Instance.Dispose();
            host?.Dispose();
        }
    }

    private static void WriteVersionHeading()
    {
        var assembly = typeof(Program).Assembly;
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        if (string.IsNullOrEmpty(product))
        {
            product = assembly.GetName().Name ?? "CosmosDBShell";
        }

        var version = ShellInterpreter.GetDisplayVersion(assembly);
        var commit = ShellInterpreter.GetDisplayCommit(assembly);
        var heading = string.IsNullOrEmpty(commit)
            ? $"{product} {version}"
            : $"{product} {version} ({commit})";
        ShellInterpreter.WriteLine(heading);
    }

    /// <summary>
    /// Pre-processes argv before <see cref="System.CommandLine"/> sees it:
    ///   1. Translates Windows-style <c>/c</c>/<c>/k</c> switches into their
    ///      POSIX equivalents (<c>-c</c>/<c>-k</c>).
    ///   2. Once <c>-c</c> or <c>-k</c> is encountered, the rest of the
    ///      command line is collapsed into a single string value, so users
    ///      don't have to quote multi-word commands:
    ///         CosmosDBShell -c help mkitem
    ///      becomes
    ///         CosmosDBShell -c "help mkitem"
    ///      App-level options must therefore come before <c>-c</c>/<c>-k</c>.
    /// </summary>
    internal static string[] NormalizeArguments(string[] args)
    {
        var result = new List<string>(args.Length);

        for (int index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            // Translate /c, /k, /C, /K to -c / -k early so all later checks
            // can treat them uniformly.
            if (argument is "/c" or "/C")
            {
                argument = "-c";
            }
            else if (argument is "/k" or "/K")
            {
                argument = "-k";
            }

            if (argument is "-c" or "-k")
            {
                result.Add(argument);
                if (index + 1 < args.Length)
                {
                    result.Add(string.Join(' ', args.Skip(index + 1)));
                }

                return [.. result];
            }

            result.Add(argument);
        }

        return [.. result];
    }

    /// <summary>
    /// Returns the argv prefix that precedes the first <c>-c</c> / <c>-k</c>
    /// (already normalized from <c>/c</c>, <c>/k</c>). Anything from the
    /// command marker onward is the consume-rest command tail and must not
    /// be inspected for app-level flags such as <c>--help</c>, <c>--version</c>,
    /// <c>--lsp</c>, or <c>--stdio</c>.
    /// </summary>
    internal static string[] TakePreCommandArgs(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] is "-c" or "-k")
            {
                return args.Take(index).ToArray();
            }
        }

        return args;
    }

    private static (RootCommand Command, OptionMap Map) BuildRootCommand()
    {
        var colorSystem = new Option<int>("--cs", () => 2, MessageService.GetString("help-ColorSystem"));

        var executeAndQuit = new Option<string?>("-c", MessageService.GetString("help-ExecuteAndQuit"));
        var executeAndContinue = new Option<string?>("-k", MessageService.GetString("help-ExecuteAndContinue"));

        var clearHistory = new Option<bool>("--clearhistory", MessageService.GetString("help-ClearHistory"));
        var connectionString = new Option<string?>("--connect", MessageService.GetString("help-ConnectionString"));

        var connectionMode = new Option<ConnectionMode?>(
            "--connect-mode",
            parseArgument: argResult =>
            {
                if (argResult.Tokens.Count == 0)
                {
                    return null;
                }

                var token = argResult.Tokens[0].Value;
                if (Enum.TryParse<ConnectionMode>(token, ignoreCase: true, out var mode))
                {
                    return mode;
                }

                argResult.ErrorMessage = MessageService.GetArgsString(
                    "help-error-BadFormatConversionError2",
                    "option",
                    "--connect-mode");
                return null;
            },
            isDefault: true,
            description: MessageService.GetString("help-ConnectionMode"));

        var connectTenant = new Option<string?>("--connect-tenant", MessageService.GetString("help-ConnectTenant"));
        var connectHint = new Option<string?>("--connect-hint", MessageService.GetString("help-ConnectHint"));
        var connectAuthorityHost = new Option<string?>("--connect-authority-host", MessageService.GetString("help-ConnectAuthorityHost"));
        var connectManagedIdentity = new Option<string?>("--connect-managed-identity", MessageService.GetString("help-ConnectManagedIdentity"));
        var connectVSCodeCredential = new Option<bool>("--connect-vscode-credential", MessageService.GetString("help-ConnectVSCodeCredential"))
        {
            IsHidden = true,
        };

        var mcpPort = new Option<int?>("--mcp", MessageService.GetString("help-McpPort"))
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var startLspServer = new Option<bool>("--lsp", MessageService.GetString("help-EnableLspServer"));
        var lspStdio = new Option<bool>("--stdio", MessageService.GetString("help-EnableLspServer"))
        {
            IsHidden = true,
        };
        var verbose = new Option<bool>("--verbose", MessageService.GetString("help-Verbose"));

        var root = new RootCommand("Cosmos DB Shell")
        {
            colorSystem,
            executeAndQuit,
            executeAndContinue,
            clearHistory,
            connectionString,
            connectionMode,
            connectTenant,
            connectHint,
            connectAuthorityHost,
            connectManagedIdentity,
            connectVSCodeCredential,
            mcpPort,
            startLspServer,
            lspStdio,
            verbose,
        };

        var map = new OptionMap(
            colorSystem,
            executeAndQuit,
            executeAndContinue,
            clearHistory,
            connectionString,
            connectionMode,
            connectTenant,
            connectHint,
            connectAuthorityHost,
            connectManagedIdentity,
            connectVSCodeCredential,
            mcpPort,
            startLspServer,
            lspStdio,
            verbose);

        return (root, map);
    }

    private static string BuildHelpText()
    {
        var (rootCommand, _) = BuildRootCommand();
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(MessageService.GetString("help-UsageHeadingText"));
        var product = typeof(Program).Assembly.GetName().Name ?? "CosmosDBShell";
        builder.AppendLine("  " + MessageService.GetArgsString("help-UsageSynopsis", "command", product.ToLowerInvariant()));
        builder.AppendLine("  " + MessageService.GetString("help-CommandTailNote"));
        builder.AppendLine();

        foreach (var symbol in rootCommand.Options)
        {
            if (symbol.IsHidden)
            {
                continue;
            }

            var aliases = string.Join(", ", symbol.Aliases);
            builder.AppendLine($"  {aliases,-32} {symbol.Description}");
        }

        // --help / --version are intercepted before parsing, so they are not
        // declared as Option<T>. Surface them in the rendered help anyway so
        // users can discover them.
        builder.AppendLine($"  {"--help, -h, -?",-32} {MessageService.GetString("help-HelpOptionDescription")}");
        builder.AppendLine($"  {"--version",-32} {MessageService.GetString("help-VersionOptionDescription")}");

        return builder.ToString();
    }

    private sealed record OptionMap(
        Option<int> ColorSystem,
        Option<string?> ExecuteAndQuit,
        Option<string?> ExecuteAndContinue,
        Option<bool> ClearHistory,
        Option<string?> ConnectionString,
        Option<ConnectionMode?> ConnectionMode,
        Option<string?> ConnectTenant,
        Option<string?> ConnectHint,
        Option<string?> ConnectAuthorityHost,
        Option<string?> ConnectManagedIdentity,
        Option<bool> ConnectVSCodeCredential,
        Option<int?> McpPort,
        Option<bool> StartLspServer,
        Option<bool> LspStdio,
        Option<bool> Verbose);

    /// <summary>
    /// Maps the most common <c>System.CommandLine</c> parse error messages
    /// to the existing <c>help-error-*</c> entries in <c>en.ftl</c> so the
    /// localized help/error strings authored for the previous parser are not
    /// silently lost. Anything not overridden falls back to the default
    /// English text from <see cref="System.CommandLine.LocalizationResources"/>.
    /// </summary>
    private sealed class LocalizedCliResources : System.CommandLine.LocalizationResources
    {
        public override string UnrecognizedCommandOrArgument(string arg) =>
            MessageService.GetArgsString("help-error-UnknownOptionError", "option", arg);

        public override string UnrecognizedArgument(string unrecognizedArg, IReadOnlyCollection<string> allowedValues) =>
            MessageService.GetArgsString("help-error-UnknownOptionError", "option", unrecognizedArg);

        public override string ExpectsOneArgument(System.CommandLine.Parsing.SymbolResult symbolResult) =>
            MessageService.GetArgsString("help-error-MissingValueOptionError", "option", symbolResult.Symbol.Name);

        public override string NoArgumentProvided(System.CommandLine.Parsing.SymbolResult symbolResult) =>
            MessageService.GetArgsString("help-error-MissingValueOptionError", "option", symbolResult.Symbol.Name);

        public override string RequiredArgumentMissing(System.CommandLine.Parsing.SymbolResult symbolResult) =>
            MessageService.GetArgsString("help-error-MissingRequiredOptionError2", "option", symbolResult.Symbol.Name);

        public override string ArgumentConversionCannotParseForOption(string value, string optionName, Type expectedType) =>
            MessageService.GetArgsString("help-error-BadFormatConversionError2", "option", optionName);
    }

    public class CosmosShellOptions
    {
        public int ColorSystem { get; set; } = 2;

        public string? ExecuteAndQuit { get; set; }

        public string? ExecuteAndContinue { get; set; }

        public bool ClearHistory { get; set; }

        public string? ConnectionString { get; set; }

        public ConnectionMode? ConnectionMode { get; set; }

        public string? ConnectTenant { get; set; }

        public string? ConnectHint { get; set; }

        public string? ConnectAuthorityHost { get; set; }

        public string? ConnectManagedIdentity { get; set; }

        public bool ConnectVSCodeCredential { get; set; }

        public int? McpPort { get; set; }

        public bool StartLspServer { get; set; }

        public bool LspStdio { get; set; }

        public bool Verbose { get; set; }
    }
}
