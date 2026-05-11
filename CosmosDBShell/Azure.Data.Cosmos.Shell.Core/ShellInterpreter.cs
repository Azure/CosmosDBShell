// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.KeyBindings;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Identity;
using Microsoft.Azure.Cosmos;
using RadLine;
using Spectre.Console;

/// <summary>
/// Provides the main interpreter logic for the Cosmos DB Shell, including command execution,
/// connection management, variable handling, and shell state management.
/// </summary>
public partial class ShellInterpreter : IDisposable
{
    internal static readonly ShellInterpreter Instance = new();

    private const int MAXHISTORYITEMS = 60;

    private const double TimeoutInSeconds = 10.0;

    private static CancellationTokenSource? currentTokenSource;

    private readonly string cfgPath;

    private LineEditor? lineEditor;

    private CancellationTokenSource editorCancelTokenSource;

    private bool disposedValue;

    private List<string> history;

    internal ShellInterpreter()
    {
        this.State = new DisconnectedState();

        // editor.KeyBindings.Add<ClearInputCommand>(ConsoleKey.Escape);
        // TODO: Support selection commands?
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this.cfgPath = Path.Combine(appData, "CosmosDBShell");
        this.history = [];
        if (!Directory.Exists(this.cfgPath))
        {
            Directory.CreateDirectory(this.cfgPath);
        }

        this.HistoryFile = Path.Combine(this.cfgPath, "cmd_history");
        if (File.Exists(this.HistoryFile))
        {
            foreach (var line in File.ReadAllLines(this.HistoryFile))
            {
                this.history.Remove(line);
                this.history.Add(line);
            }
        }

        Console.CancelKeyPress += this.Console_CancelKeyPress;
        this.editorCancelTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// Gets the line editor instance used by the shell, or <c>null</c> if not available.
    /// </summary>
    public LineEditor? Editor { get => this.lineEditor ??= this.CreateLineEditor(); }

    /// <summary>
    /// Gets or sets a value indicating whether the shell is currently running.
    /// </summary>
    public bool IsRunning { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the shell will echo commands before executing them in scripts.
    /// </summary>
    public bool Echo { get; set; } = true;

    internal static CancellationTokenSource TokenSource
    {
        get
        {
            currentTokenSource?.Dispose();
            return currentTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutInSeconds));
        }
    }

    internal static CancellationTokenSource UserCancellationTokenSource
    {
        get
        {
            currentTokenSource?.Dispose();
            return currentTokenSource = new CancellationTokenSource();
        }
    }

    internal static char CSVSeparator
    {
        get
        {
            var sep = Environment.GetEnvironmentVariable("COSMOSDB_SHELL_CSVSEP");
            if (!string.IsNullOrEmpty(sep))
            {
                return sep[0];
            }

            return ';';
        }
    }

    internal Dictionary<string, DefStatement> Functions { get; } = [];

    internal string HistoryFile { get; private set; }

    internal IReadOnlyList<string> History => this.history;

    internal string? LastBuffer { get; set; }

    internal string? OriginalString { get; set; }

    internal string? CurrentScriptFileName { get; set; }

    internal string? CurrentScriptContent { get; set; }

    internal CommandRunner App { get; private set; } = new CommandRunner();

    internal string? StdOutRedirect { get; set; }

    internal string? ErrOutRedirect { get; set; }

    internal bool AppendOutRedirection { get; set; }

    internal bool AppendErrRedirection { get; set; }

    internal State State { get; set; }

    internal Program.CosmosShellOptions? Options { get; set; }

    internal int? McpPort { get; set; }

    internal Queue<VariableContainer> VariableContainers { get; } = new();

    /// <summary>
    /// Create a new instance of the <see cref="ShellInterpreter"/> class.
    /// </summary>
    /// <returns>A new instance of the <see cref="ShellInterpreter"/> class.</returns>
    public static ShellInterpreter CreateInstance()
    {
        return new ShellInterpreter();
    }

    /// <summary>
    /// Writes the specified message to the standard output stream, using the specified format parameters.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="par">An array of objects to format.</param>
    public static void WriteLine(string message, params object[] par)
    {
        Console.WriteLine(message, par);
    }

    /// <summary>
    /// Writes the specified message to the standard output stream.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public static void WriteLine(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// Writes an empty line to the standard output stream.
    /// </summary>
    public static void WriteLine()
    {
        Console.WriteLine();
    }

    /// <summary>
    /// Writes the specified message to the standard output stream, using the specified format parameters.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="par">An array of objects to format.</param>
    public static void Write(string message, params object[] par)
    {
        Console.Write(message, par);
    }

    /// <summary>
    /// Writes the specified message to the standard output stream.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public static void Write(string message)
    {
        Console.Write(message);
    }

    /// <summary>
    /// Prompts the user for confirmation with a yes/no question.
    /// </summary>
    /// <param name="message">The message to display to the user.</param>
    /// <returns><c>true</c> if the user confirms; otherwise, <c>false</c>.</returns>
    public static bool Confirm(string message)
    {
        var yes = char.ToUpper(MessageService.GetString("yes_char")[0]);
        var no = char.ToUpper(MessageService.GetString("no_char")[0]);

        while (true)
        {
            Console.Write($"{MessageService.GetString(message)} ({yes}/{no})?");
            var key = Console.ReadKey();
            WriteLine();
            if (char.ToUpper(key.KeyChar) == yes)
            {
                return true;
            }

            if (char.ToUpper(key.KeyChar) == no || key.Key == ConsoleKey.Escape)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Cancels the current prompt operation, including any ongoing editor or command input.
    /// </summary>
    public void CancelPrompt()
    {
        currentTokenSource?.Cancel();
        this.editorCancelTokenSource.Cancel();
        this.editorCancelTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// Executes a command asynchronously in the shell interpreter.
    /// </summary>
    /// <param name="command">The command string to execute.</param>
    /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="CommandState"/> representing the result of the command execution.</returns>
    public async Task<CommandState> ExecuteCommandAsync(string command, CancellationToken token)
    {
        var state = new CommandState();
        state.SetFormat(Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT"));

        // Snapshot redirect state so a '>' / '2>' on this command does not leak into
        // the next command executed against this interpreter instance.
        var savedStdOut = this.StdOutRedirect;
        var savedAppendOut = this.AppendOutRedirection;
        var savedErrOut = this.ErrOutRedirect;
        var savedAppendErr = this.AppendErrRedirection;

        try
        {
            try
            {
                state = await this.RunCommandAsync(state, command, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new CommandState();
            }
            catch (TaskCanceledException e)
            {
                var shellException = new ShellException(CommandException.GetDisplayMessage(e), e);
                this.ReportExecutionError(shellException);
                return new ErrorCommandState(shellException);
            }
            catch (Exception e)
            {
                this.ReportExecutionError(e);
                var inner = e is PositionalException pe ? (pe.InnerException ?? pe) : e;
                return new ErrorCommandState(inner);
            }

            if (token.IsCancellationRequested)
            {
                return state;
            }

            return this.PrintState(state);
        }
        finally
        {
            this.StdOutRedirect = savedStdOut;
            this.AppendOutRedirection = savedAppendOut;
            this.ErrOutRedirect = savedErrOut;
            this.AppendErrRedirection = savedAppendErr;
        }
    }

    /// <summary>
    /// Redirects the specified text to the standard output redirection file, if set.
    /// Appends or overwrites the file based on the <see cref="AppendOutRedirection"/> flag.
    /// Ensures a newline is present at the end of the redirected text.
    /// </summary>
    /// <param name="text">The text to redirect to the output file.</param>
    public void Redirect(string text)
    {
        if (this.StdOutRedirect == null)
        {
            return;
        }

        if (this.AppendOutRedirection)
        {
            File.AppendAllText(this.StdOutRedirect, text);
        }
        else
        {
            File.WriteAllText(this.StdOutRedirect, text);
        }

        if (!text.EndsWith(Environment.NewLine))
        {
            File.AppendAllText(this.StdOutRedirect, Environment.NewLine);
        }
    }

    /// <summary>
    /// Releases all resources used by the <see cref="ShellInterpreter"/>.
    /// </summary>
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    internal static string GetDisplayVersion(Assembly assembly)
    {
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+')[0];
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    internal static string GetDisplayCommit(Assembly assembly)
    {
        return ExtractCommitMetadata(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    internal static string ExtractCommitMetadata(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return string.Empty;
        }

        var plusIndex = informationalVersion.IndexOf('+');
        if (plusIndex < 0 || plusIndex >= informationalVersion.Length - 1)
        {
            return string.Empty;
        }

        // Metadata may carry multiple dot-separated parts when the build pipeline
        // sets /p:InformationalVersion=<pkg>+<sha> and the SDK target
        // AddSourceRevisionToInformationalVersion then also appends the
        // SourceRevisionId (producing "<sha>.<sha>"). Collapse identical repeats
        // and preserve distinct segments joined by '.'.
        var parts = informationalVersion[(plusIndex + 1)..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var distinct = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (distinct.Count == 0 || !string.Equals(distinct[^1], part, StringComparison.Ordinal))
            {
                distinct.Add(part);
            }
        }

        return string.Join('.', distinct);
    }

    internal static string GetRepositoryUrl(Assembly assembly)
    {
        foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attr.Key, "RepositoryUrl", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(attr.Value))
            {
                return attr.Value;
            }
        }

        return string.Empty;
    }

    internal static void ReportError(string message, params object[] par)
    {
        AnsiConsole.MarkupLine("[" + Theme.ErrorColorName + "]" + Markup.Escape(message) + "[/]", par);
    }

    internal ShellObject GetVariable(string name)
    {
        var scope = this.GetScope(name);
        if (scope?.TryGetValue(name, out var value) == true)
        {
            return value;
        }

        throw new ShellException(MessageService.GetArgsString("error-variable_not_set", "name", name));
    }

    internal void PrintVersion(CommandState? commandState)
    {
        var version = GetDisplayVersion(typeof(VersionCommand).Assembly);
        var versionString = MessageService.GetArgsString("command-version", "version", version);
        AnsiConsole.MarkupLine(versionString);

        var port = this.McpPort;
        if (port != null)
        {
            var mcpPortString = MessageService.GetArgsString("command-version-mcp", "mcp_port", port?.ToString() ?? string.Empty);
            AnsiConsole.MarkupLine("[" + Theme.WarningColorName + "]" + mcpPortString + "[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-version-mcp-off"));
        }

        var repoUrl = GetRepositoryUrl(typeof(VersionCommand).Assembly);
        if (!string.IsNullOrEmpty(repoUrl))
        {
            var repoString = MessageService.GetArgsString("command-version-repo", "url", repoUrl);
            AnsiConsole.MarkupLine(repoString);
        }

        if (commandState != null)
        {
            var json = new Dictionary<string, object?>
            {
                ["version"] = version,
                ["mcpEnabled"] = port != null,
                ["mcpPort"] = port, // will be null if not enabled
                ["mcpStatus"] = port != null ? "on" : "off",
                ["repository"] = repoUrl,
            };

            var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(json);
            commandState.Result = new ShellJson(jsonElement);
            commandState.IsPrinted = true;
        }
    }

    internal async Task<int> RunAsync()
    {
        var result = 0;
        this.PrintVersion(null);
        WriteLine(MessageService.GetString("shell-ready"));
        while (this.IsRunning)
        {
            this.StdOutRedirect = null;
            try
            {
                this.ClearHighlightStatement();
                var input = this.Editor != null ? await this.Editor.ReadLine(this.editorCancelTokenSource.Token) : PromptFallback();
                if (input is not { } command)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(command))
                {
                    this.history.Remove(command);
                    this.history.Add(command);
                    this.SaveHistory();
                    CancellationToken token = TokenSource.Token;
                    await this.ExecuteCommandAsync(command, token);
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        return result;
    }

    internal async Task<CommandState> RunCommandAsync(CommandState currentState, string commandText, CancellationToken token)
    {
        var lexer = new Lexer(commandText);
        var parser = new StatementParser(lexer);

        foreach (var statements in parser.ParseStatements())
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            // Run the parsed statements
            currentState = await statements.RunAsync(this, currentState, token);
            if (currentState.IsError)
            {
                break;
            }
        }

        if (parser.Errors.HasErrors)
        {
            return new ParserErrorCommandState(parser.Errors);
        }

        /*
        var line = this.parser.Parse(commandText);
        if (line.StdOutRedirect.Length > 0)
        {
            this.StdOutRedirect = line.StdOutRedirect;
            this.AppendOutRedirection = line.AppendRedirect;
        }

        foreach (var cmd in line.Command)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            for (int i = 0; i < cmd.Arguments.Length; i++)
            {
                // Replace parameters in the command
                cmd.Arguments[i] = this.ReplaceJSonArgument(cmd.Arguments[i], currentState);
            }

#if DEBUG
            if (cmd.JSonPath == "?" || cmd.JSonPath.ToString().Equals("GEN_DOC", StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
#endif
            if (cmd.Arguments.Length > 0 && !this.App.IsExternal(cmd) && IsHelpOption(cmd.Arguments[0]) && !this.App.IsExternal(commandText))
            {
                HelpCommand.PrintCommandHelp(cmd.JSonPath, this.App);
                continue;
            }

            if (File.Exists(cmd.JSonPath))
            {
                currentState = await this.RunScript(currentState, cmd, token);
                continue;
            }

            currentState = await this.App.RunAsync(this, currentState, cmd, commandText, token);
            if (currentState.IsError)
            {
                break;
            }
        }
        */
        return currentState;
    }

    internal async Task ConnectAsync(string connectionString, string? loginHint = null, ConnectionMode? mode = null, string? tenantId = null, string? authorityHost = null, string? managedIdentityClientId = null, bool useVSCodeCredential = false, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        Uri? authorityHostUri = null;
        if (!string.IsNullOrWhiteSpace(authorityHost))
        {
            if (!Uri.TryCreate(authorityHost, UriKind.Absolute, out authorityHostUri))
            {
                throw new ShellException($"Invalid authority host URL: '{authorityHost}'");
            }
        }

        CosmosClient? client = null;

        // Step 1: Resolve account key (from connection string, env variable, or emulator well-known key)
        bool isEmulator = ParsedDocDBConnectionString.IsLocalEmulatorEndpoint(connectionString);
        if (isEmulator)
        {
            WriteLine(MessageService.GetString("command-connect-emulator-detected"));
        }

        bool hasKey = ParsedDocDBConnectionString.TryParseDocDBConnectionString(connectionString, out var parsedCs) && parsedCs!.HasMasterKey;

        if (isEmulator)
        {
            // Always route emulator through BuildEmulatorConnectionString to ensure
            // DisableServerCertificateValidation=True is present.
            var endpoint = ParsedDocDBConnectionString.ExtractEndpoint(connectionString);
            string? accountKey = parsedCs?.MasterKey;

            if (accountKey == null)
            {
                var envKey = Environment.GetEnvironmentVariable("COSMOSDB_SHELL_ACCOUNT_KEY");
                if (!string.IsNullOrEmpty(envKey))
                {
                    accountKey = envKey;
                }
            }

            connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(endpoint, accountKey);
            hasKey = true;
        }
        else if (!hasKey)
        {
            var envKey = Environment.GetEnvironmentVariable("COSMOSDB_SHELL_ACCOUNT_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                var endpoint = ParsedDocDBConnectionString.ExtractEndpoint(connectionString);
                connectionString = $"AccountEndpoint={endpoint};AccountKey={envKey};";
                hasKey = true;
            }
        }

        if (hasKey)
        {
            WriteLine(MessageService.GetString("shell-connect-key-auth"));
            var keyMode = mode ?? (isEmulator ? ConnectionMode.Gateway : ConnectionMode.Direct);
            var keyOptions = CreateClientOptions(connectionString, keyMode);
            client = new CosmosClient(connectionString, keyOptions);

            AccountProperties keyProps;
            try
            {
                keyProps = await ReadAccountAsync(client, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new ShellException(MessageService.GetString("error-connection_failed"), ex);
            }

            WriteLine(MessageService.GetArgsString("command-connect-connected", "account", keyProps.Id));
            this.Connect(client);
            return;
        }

        // Token-based auth paths
        var requestedMode = mode ?? ConnectionMode.Direct;
        var options = CreateClientOptions(connectionString, requestedMode);

        // Step 2: VisualStudioCodeCredential (when launched from VS Code extension)
        if (client == null && useVSCodeCredential)
        {
            WriteLine(MessageService.GetString("shell-connect-vscode-credential-auth"));
            var endpoint = ParsedDocDBConnectionString.ExtractEndpoint(connectionString);

            var vscOptions = new VisualStudioCodeCredentialOptions();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                vscOptions.TenantId = tenantId;
            }

            if (authorityHostUri != null)
            {
                vscOptions.AuthorityHost = authorityHostUri;
            }

            var vscCredential = new VisualStudioCodeCredential(vscOptions);
            client = new CosmosClient(endpoint, vscCredential, options);

            try
            {
                var vscProps = await ReadAccountAsync(client, token);
                WriteLine(MessageService.GetArgsString("command-connect-connected", "account", vscProps.Id));
                this.Connect(client);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is AuthenticationFailedException or CredentialUnavailableException)
            {
                client.Dispose();
                client = null;
                WriteLine(MessageService.GetString("shell-connect-vscode-credential-fallback"));
            }
            catch (Exception)
            {
                client?.Dispose();
                client = null;
                throw;
            }
        }

        // Step 3: Static token from COSMOSDB_SHELL_TOKEN environment variable
        var envToken = Environment.GetEnvironmentVariable("COSMOSDB_SHELL_TOKEN");
        if (client == null && !string.IsNullOrEmpty(envToken))
        {
            WriteLine(MessageService.GetString("shell-connect-static-token-auth"));
            var endpoint = ParsedDocDBConnectionString.ExtractEndpoint(connectionString);
            var credential = new StaticTokenCredential(envToken);
            if (credential.HasJwtExpiry)
            {
                var remaining = credential.ExpiresOn - DateTimeOffset.UtcNow;
                var timeSpan = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                WriteLine(MessageService.GetArgsString("shell-connect-static-token-expiry", "timespan", $"{timeSpan:hh\\:mm\\:ss}", "expiration", credential.ExpiresOn.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
            }

            client = new CosmosClient(endpoint, credential, options);

            AccountProperties tokenProps;
            try
            {
                tokenProps = await ReadAccountAsync(client, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new ShellException(MessageService.GetString("error-connection_failed"), ex);
            }

            WriteLine(MessageService.GetArgsString("command-connect-connected", "account", tokenProps.Id));
            this.Connect(client);
            return;
        }

        // Step 4: Managed identity
        if (client == null && !string.IsNullOrWhiteSpace(managedIdentityClientId))
        {
            WriteLine(MessageService.GetArgsString("shell-connect-managed-identity-auth", "clientId", managedIdentityClientId));
            var endpoint = ParsedDocDBConnectionString.ExtractEndpoint(connectionString);
            var miOptions = new ManagedIdentityCredentialOptions(ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId));
            if (authorityHostUri != null)
            {
                miOptions.AuthorityHost = authorityHostUri;
            }

            var credential = new ManagedIdentityCredential(miOptions);
            client = new CosmosClient(endpoint, credential, options);

            AccountProperties miProps;
            try
            {
                miProps = await ReadAccountAsync(client, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new ShellException(MessageService.GetString("error-connection_failed"), ex);
            }

            WriteLine(MessageService.GetArgsString("command-connect-connected", "account", miProps.Id));
            this.Connect(client);
            return;
        }

        // Step 5: Entra ID interactive (--tenant or --hint provided)
        if (client == null && (!string.IsNullOrWhiteSpace(tenantId) || !string.IsNullOrWhiteSpace(loginHint)))
        {
            var endpoint = ParsedDocDBConnectionString.ExtractEndpoint(connectionString);

            var browserOptions = new InteractiveBrowserCredentialOptions
            {
                RedirectUri = new Uri(ConnectCommand.EntraRedirectUrl),
            };
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                browserOptions.TenantId = tenantId;
            }

            if (!string.IsNullOrWhiteSpace(loginHint))
            {
                browserOptions.LoginHint = loginHint;
            }

            if (authorityHostUri != null)
            {
                browserOptions.AuthorityHost = authorityHostUri;
            }

            WriteLine(MessageService.GetString("shell-connect-browser-auth"));
            var browserCredential = new InteractiveBrowserCredential(browserOptions);
            client = new CosmosClient(endpoint, browserCredential, options);

            try
            {
                var entraProps = await ReadAccountAsync(client, token);
                WriteLine(MessageService.GetArgsString("command-connect-connected", "account", entraProps.Id));
                this.Connect(client);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is AuthenticationFailedException or CredentialUnavailableException)
            {
                // Browser auth failed, fall back to device code
                WriteLine(MessageService.GetString("shell-connect-devicecode-fallback"));
                client.Dispose();

                var deviceCodeOptions = new DeviceCodeCredentialOptions
                {
                    DeviceCodeCallback = (code, cancellationToken) =>
                    {
                        ShellInterpreter.WriteLine(code.Message);
                        return Task.CompletedTask;
                    },
                };
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    deviceCodeOptions.TenantId = tenantId;
                }

                if (authorityHostUri != null)
                {
                    deviceCodeOptions.AuthorityHost = authorityHostUri;
                }

                var deviceCodeCredential = new DeviceCodeCredential(deviceCodeOptions);
                client = new CosmosClient(endpoint, deviceCodeCredential, options);

                AccountProperties dcProps;
                try
                {
                    dcProps = await ReadAccountAsync(client, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    client.Dispose();
                    throw;
                }
                catch (Exception dcEx)
                {
                    client.Dispose();
                    throw new ShellException(MessageService.GetString("error-connection_failed"), dcEx);
                }

                WriteLine(MessageService.GetArgsString("command-connect-connected", "account", dcProps.Id));
                this.Connect(client);
                return;
            }
        }

        // Step 6: DefaultAzureCredential (endpoint only, or only --authority-host)
        if (client == null)
        {
            var endpoint = ParsedDocDBConnectionString.ExtractEndpoint(connectionString);
            WriteLine(MessageService.GetString("shell-connect-default-auth"));
            var dacOptions = new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = false,
            };
            if (authorityHostUri != null)
            {
                dacOptions.AuthorityHost = authorityHostUri;
            }

            var dacCredential = new DefaultAzureCredential(dacOptions);
            client = new CosmosClient(endpoint, dacCredential, options);

            try
            {
                var dacProps = await ReadAccountAsync(client, token);
                WriteLine(MessageService.GetArgsString("command-connect-connected", "account", dacProps.Id));
                this.Connect(client);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is AuthenticationFailedException or CredentialUnavailableException)
            {
                client.Dispose();
                throw new ShellException(MessageService.GetString("error-connection_failed"), ex);
            }
        }
    }

    private static async Task<AccountProperties> ReadAccountAsync(CosmosClient client, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return await client.ReadAccountAsync().WaitAsync(token);
    }

    /// <summary>
    /// Connects to a client & disposes old state.
    /// </summary>
    internal void Connect(CosmosClient client)
    {
        this.State?.Dispose();
        this.State = new ConnectedState(client);
        CosmosCompleteCommand.ClearDatabases();
        CosmosCompleteCommand.ClearContainers();
    }

    /// <summary>
    /// Disconnects & disposes the old state.
    /// </summary>
    internal void Disconnect()
    {
        this.State?.Dispose();
        this.State = new DisconnectedState();
    }

    internal void PrintCommand(string cmdString)
    {
        // Print the shell prompt similar to how it appears when typing command
        //        AnsiConsole.Markup(new CosmosShellPrompt(this).GetPromptString());
        //        AnsiConsole.Write(" ");
        var txt = ((IHighlighter)Instance).BuildHighlightedText(cmdString);
        AnsiConsole.Write(txt);
        AnsiConsole.WriteLine(); // Ensure the next output starts on a new line

        this.history.Remove(cmdString);
        this.history.Add(cmdString);
        this.Editor?.History.Add(cmdString);
    }

    internal CommandState PrintState(CommandState state)
    {
        if (state.IsPrinted)
        {
            // command already printed the state.
            return state;
        }

        try
        {
            string? output;

            if (state.Result?.DataType == Parser.DataType.Json)
            {
                // When writing JSON to the terminal (not redirected to a file), apply
                // syntax highlighting using the configured Spectre.Console theme. File
                // redirection still receives plain text so downstream tooling and tests
                // are unaffected.
                if (state.OutputFormat == OutputFormat.JSon && string.IsNullOrEmpty(this.StdOutRedirect))
                {
                    var element = (JsonElement?)state.Result.ConvertShellObject(Parser.DataType.Json);
                    if (element.HasValue)
                    {
                        AnsiConsole.MarkupLine(JsonOutputHighlighter.BuildMarkup(element.Value));
                        state.Result = null;
                        return state;
                    }
                }

                output = state.GenerateOutputText();
            }
            else
            {
                output = state.Result?.ConvertShellObject(Parser.DataType.Text) as string;
            }

            if (output != null)
            {
                if (string.IsNullOrEmpty(this.StdOutRedirect))
                {
                    WriteLine(output);
                }
                else
                {
                    this.Redirect(output);
                }
            }

            // Clear the result after printing
            state.Result = null;
        }
        catch (Exception e)
        {
            if (this.Options?.Verbose == true)
            {
                AnsiConsole.Markup($"[{Theme.ErrorColorName}]PrintState: [/]");
                AnsiConsole.WriteException(e);
            }
            else
            {
                var m = Markup.Escape(e.Message);
                AnsiConsole.MarkupLine($"[{Theme.ErrorColorName}]PrintState:{m}[/]");
                if (e.InnerException != null)
                {
                    WriteLine(e.InnerException.ToString());
                }
            }

            return new ErrorCommandState(e);
        }

        return state;
    }

    internal void DeclareFunction(DefStatement defStatement)
    {
        this.Functions[defStatement.Name] = defStatement;
    }

    internal void SetVariable(string variableName, ShellObject value)
    {
        // Ensure we have at least one variable container (global scope)
        if (this.VariableContainers.Count == 0)
        {
            this.VariableContainers.Enqueue(new VariableContainer());
        }

        // When running inside a script, always write to the current (script) frame.
        // This ensures script-local assignments don't modify variables in caller scopes.
        // Outside of scripts, search for existing variable to maintain back-compat.
        VariableContainer currentScope;
        if (!string.IsNullOrEmpty(this.CurrentScriptFileName))
        {
            // Script execution: always use current frame (script-local by default)
            currentScope = this.VariableContainers.Last();
        }
        else
        {
            // Interactive/global: update existing variable if found, else use current frame
            currentScope = this.GetScope(variableName) ?? this.VariableContainers.Last();
        }

        var targetType = value.DataType;

        // ConvertShellObject the value to get the actual result
        var evaluatedValue = value.ConvertShellObject(targetType);

        // Convert the evaluated value back to a ShellObject
        ShellObject shellValue = evaluatedValue switch
        {
            string s => new ShellText(s),
            int i => new ShellNumber(i),
            bool b => new ShellBool(b),
            double d => new ShellDecimal(d),
            JsonElement json => new ShellJson(json),
            ShellObject so => so,
            _ => new ShellText(evaluatedValue?.ToString() ?? string.Empty),
        };

        // Store the variable in the current scope
        currentScope.Set(variableName, shellValue);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="ShellInterpreter"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">
    /// <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposedValue)
        {
            if (disposing)
            {
                Console.CancelKeyPress -= this.Console_CancelKeyPress;
                currentTokenSource?.Dispose();
                this.editorCancelTokenSource?.Dispose();
                this.State?.Dispose();
            }

            this.disposedValue = true;
        }
    }

    private static string? PromptFallback()
    {
        Console.Write(CosmosShellPrompt.PromptText + "> ");
        return Console.ReadLine();
    }

    private static CosmosClientOptions CreateClientOptions(string connectionString, ConnectionMode requestedMode)
    {
        var options = new CosmosClientOptions
        {
            ApplicationName = "CosmosDBShell",
            ConnectionMode = requestedMode,
            CosmosClientTelemetryOptions = new CosmosClientTelemetryOptions(),
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions()
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            },
        };

        // do not check certificates for emulator - work around on osx issue
        if (ParsedDocDBConnectionString.IsLocalEmulatorEndpoint(connectionString))
        {
            options.ServerCertificateCustomValidationCallback = (cert, chain, errors) => true;
        }

        return options;
    }

    private LineEditor CreateLineEditor()
    {
        try
        {
            var lineEditor = new LineEditor()
            {
                Prompt = new CosmosShellPrompt(this),
                LineDecorationRenderer = new CosmosCompletionRenderer(this),
                Highlighter = this,
            };
            lineEditor.KeyBindings.Add<PreviousHistoryCommand>(ConsoleKey.UpArrow);
            lineEditor.KeyBindings.Add<NextHistoryCommand>(ConsoleKey.DownArrow);

            lineEditor.KeyBindings.Add<ClearCurrentLineCommand>(ConsoleKey.Escape);
            lineEditor.KeyBindings.Add<ClearScreenCommand>(ConsoleKey.L, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<MoveToStartOfLineCommand>(ConsoleKey.A, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<MoveToEndOfLineCommand>(ConsoleKey.E, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<DeleteToStartOfLineCommand>(ConsoleKey.U, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<DeleteToEndOfLineCommand>(ConsoleKey.K, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<DeletePreviousWordCommand>(ConsoleKey.W, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<PreviousHistoryCommand>(ConsoleKey.P, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<NextHistoryCommand>(ConsoleKey.N, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<MoveCursorLeftCommand>(ConsoleKey.B, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add<MoveCursorRightCommand>(ConsoleKey.F, ConsoleModifiers.Control);
            lineEditor.KeyBindings.Add(ConsoleKey.D, ConsoleModifiers.Control, () => new ExitShellCommand(this));
            lineEditor.KeyBindings.Add(ConsoleKey.R, ConsoleModifiers.Control, () => new ReverseSearchHistoryCommand(this));
            lineEditor.KeyBindings.Add(ConsoleKey.S, ConsoleModifiers.Control, () => new ReverseSearchHistoryCommand(this, startsForward: true));
            lineEditor.KeyBindings.Add(ConsoleKey.Tab, () => new CosmosCompleteCommand(this, AutoComplete.Next));
            lineEditor.KeyBindings.Add(ConsoleKey.Tab, ConsoleModifiers.Control, () => new CosmosCompleteCommand(this, AutoComplete.Previous));
            foreach (var line in this.history)
            {
                lineEditor.History.Add(line);
            }

            return lineEditor;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return new LineEditor();
        }
    }

    private VariableContainer? GetScope(string name)
    {
        foreach (var container in this.VariableContainers.Reverse())
        {
            if (container.Variables.ContainsKey(name))
            {
                return container;
            }
        }

        return null;
    }

    private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        this.CancelPrompt();
        WriteLine("̂C");
    }

    private void SaveHistory()
    {
        if (this.history.Count > MAXHISTORYITEMS)
        {
            this.history = [.. this.history.Skip(this.history.Count - MAXHISTORYITEMS)];
        }

        File.WriteAllLines(this.HistoryFile, this.history);
    }

    private void ReportExecutionError(Exception e)
    {
        if (e is PositionalException pe)
        {
            this.ReportPositionalError(pe);
            return;
        }

        var prefix = e is CommandException ce ? $"{ce.Command}: " : string.Empty;
        var showInner = e is not ShellException && e.InnerException != null;

        if (this.ErrOutRedirect != null)
        {
            var errTxt = this.Options?.Verbose == true
                ? e.ToString()
                : prefix + e.Message + (showInner ? Environment.NewLine + e.InnerException!.ToString() : string.Empty);
            if (this.AppendErrRedirection)
            {
                File.AppendAllText(this.ErrOutRedirect, errTxt);
            }
            else
            {
                File.WriteAllText(this.ErrOutRedirect, errTxt);
            }
        }
        else if (this.Options?.Verbose == true)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                AnsiConsole.MarkupLine(Markup.Escape(prefix.TrimEnd()));
            }

            AnsiConsole.WriteException(e, new ExceptionSettings
            {
                Format = ExceptionFormats.ShortenPaths,
            });
        }
        else
        {
            var m = Markup.Escape(e.Message);
            AnsiConsole.MarkupLine($"{prefix}[{Theme.ErrorColorName}]{m}[/]");
            if (showInner)
            {
                AnsiConsole.WriteLine(e.InnerException!.ToString());
            }
        }
    }

    private void ReportPositionalError(PositionalException pe)
    {
        if (this.ErrOutRedirect != null)
        {
            var errorMessage = $"[{Path.GetFileName(pe.FileName)}:{pe.Line}:{pe.Column}]: error: {pe.Message}";
            if (pe.LineText != null)
            {
                errorMessage += Environment.NewLine + pe.LineText;
                errorMessage += Environment.NewLine + new string(' ', Math.Max(0, pe.Column - 1)) + "^";
            }

            if (this.AppendErrRedirection)
            {
                File.AppendAllText(this.ErrOutRedirect, errorMessage);
            }
            else
            {
                File.WriteAllText(this.ErrOutRedirect, errorMessage);
            }
        }
        else
        {
            var m = Markup.Escape(pe.Message);
            AnsiConsole.MarkupLine($"{Markup.Escape($"{pe.FileName}:{pe.Line}:{pe.Column}:")} [{Theme.ErrorColorName}]error:[/] {m}");
            if (pe.LineText != null)
            {
                AnsiConsole.MarkupLine($"  [{Theme.MutedColorName}]{Markup.Escape(pe.LineText)}[/]");
                AnsiConsole.MarkupLine($"  [{Theme.ErrorColorName}]{new string(' ', Math.Max(0, pe.Column - 1))}^[/]");
            }
        }
    }

    /*
        private void PrintReadmeSection(string cmdStr)
        {
            if (App.commands.TryGetValue(cmdStr, out var cmd))
            {
                ShellInterpreter.Instance.WriteLine("###  " + cmd.CommandName);
                ShellInterpreter.Instance.WriteLine(cmd.Description);
                ShellInterpreter.Instance.WriteLine();
                ShellInterpreter.Instance.WriteLine("```");
                Console.Write($"Usage: {cmd.CommandName} ");

                foreach (var p in cmd.Options)
                {
                    Console.Write("[-" + p.JSonPath[0]);

                    if (!p.PropertyInfo.PropertyType.IsAssignableFrom(typeof(bool)))
                    {
                        Console.Write(" <ARG>");

                    }

                    Console.Write("] ");
                }

                foreach (var p in cmd.Parameters)
                {
                    var name = p.JSonPath;
                    if (name == null)
                    {
                        continue;
                    }
                    if (p.IsRequired)
                    {
                        Console.Write(name + " ");
                    }
                    else
                    {
                        Console.Write($"[{name}] ");
                    }
                }
                ShellInterpreter.Instance.WriteLine();
                ShellInterpreter.Instance.WriteLine();

                if (cmd.Parameters.Count > 0)
                {
                    ShellInterpreter.Instance.WriteLine($"Arguments:");
                    foreach (var p in cmd.Parameters)
                    {
                        const int ARG_PADDING = 16;
                        if (!p.IsRequired)
                        {
                            Console.Write($"    [{p.JSonPath}]".PadRight(ARG_PADDING));
                        }
                        else
                        {
                            Console.Write($"    {p.JSonPath}".PadRight(ARG_PADDING));
                        }
                        Console.Write(p.GetDescription(cmd.CommandName));

                        if (!p.IsRequired)
                        {
                            Console.Write($" (Optional)");
                        }
                        ShellInterpreter.Instance.WriteLine();
                    }
                    ShellInterpreter.Instance.WriteLine();
                }

                if (cmd.Options.Count > 0)
                {
                    ShellInterpreter.Instance.WriteLine($"Options:");
                    const int ARG_PADDING = 16;
                    foreach (var p in cmd.Options)
                    {
                        StringBuilder sb = new();
                        foreach (var n in p.JSonPath) {
                            if (sb.Length > 0)
                            {
                                sb.Append(",  ");
                            }
                            sb.Append('-');
                            sb.Append(n);
                        }
                        Console.Write($"    {sb}".PadRight(ARG_PADDING));
                        ShellInterpreter.Instance.WriteLine(" " + p.Description);
                    }
                }
            }
            else
            {
                AnsiConsole.Markup($"[{Theme.ErrorColorName}]Error:[/]");
                ShellInterpreter.Instance.WriteLine($"{cmdStr} not found.");
            }
            ShellInterpreter.Instance.WriteLine("```");
        }
        */
}
