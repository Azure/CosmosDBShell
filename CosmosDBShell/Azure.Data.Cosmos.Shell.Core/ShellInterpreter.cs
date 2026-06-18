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
using global::Azure.Core;
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

    private const int OptionalArmDiscoveryTimeoutSeconds = 3;

    private const string EncodedHistoryLinePrefix = "CosmosDBShellHistoryV1:";

    // Sentinel written immediately after the prefix by EncodeHistoryLine so that
    // DecodeHistoryLine can unambiguously tell a value it produced apart from a
    // user command that just happens to start with the prefix string.
    private const string EncodedHistoryLineMarker = "E:";

    private static CancellationTokenSource? currentTokenSource;

    private readonly string cfgPath;

    private LineEditor? lineEditor;

    private CosmosShellPrompt? cosmosShellPrompt;

    private System.Text.StringBuilder? pendingMultiLineBuffer;

    private bool pendingMultiLineSuppressesNewline;

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
                var decoded = DecodeHistoryLine(line);
                this.history.Remove(decoded);
                this.history.Add(decoded);
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
        using var activity = TracingBootstrap.StartCommandActivity("cosmosdbshell.command");
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
                this.ReportExecutionError(shellException, command);
                return new ErrorCommandState(shellException);
            }
            catch (Exception e)
            {
                this.ReportExecutionError(e, command);
                var inner = e is PositionalException pe ? (pe.InnerException ?? pe) : e;
                return new ErrorCommandState(inner);
            }

            if (token.IsCancellationRequested)
            {
                return state;
            }

            if (state is ParserErrorCommandState parserErrorState)
            {
                this.ReportParserErrors(parserErrorState.Errors, command);
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
        AnsiConsole.MarkupLine(Theme.FormatError(message), par);
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
            AnsiConsole.MarkupLine(Theme.FormatWarning(mcpPortString));
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

        // First-run hint: if the shell starts without a connection, point users at
        // the `connect` command. Otherwise users can land at the prompt with no
        // obvious next step (see issue #81).
        if (this.State is DisconnectedState)
        {
            AnsiConsole.MarkupLine(Theme.FormatWarning(MessageService.GetString("shell-not_connected_hint")));
        }

        while (this.IsRunning)
        {
            this.StdOutRedirect = null;
            try
            {
                this.ClearHighlightStatement();
                var input = this.Editor != null ? await this.Editor.ReadLine(this.editorCancelTokenSource.Token) : PromptFallback();
                var command = ProcessInteractiveLine(
                    input,
                    ref this.pendingMultiLineBuffer,
                    ref this.pendingMultiLineSuppressesNewline,
                    this.cosmosShellPrompt);
                if (command == null)
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
                this.pendingMultiLineBuffer = null;
                this.pendingMultiLineSuppressesNewline = false;
                if (this.cosmosShellPrompt != null)
                {
                    this.cosmosShellPrompt.InContinuation = false;
                }
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

    internal async Task ConnectAsync(string connectionString, string? loginHint = null, ConnectionMode? mode = null, string? tenantId = null, string? authorityHost = null, string? managedIdentityClientId = null, bool useVSCodeCredential = false, string? subscriptionId = null, string? resourceGroupName = null, CancellationToken token = default)
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
            var keyOptions = CreateClientOptions(keyMode);
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
                if (isEmulator)
                {
                    throw new ShellException(GetLocalEmulatorConnectionFailureMessage(client.Endpoint), ex);
                }

                throw new ShellException(MessageService.GetString("error-connection_failed"), ex);
            }

            WriteLine(MessageService.GetArgsString("command-connect-connected", "account", keyProps.Id));
            this.Connect(client);
            return;
        }

        // Token-based auth paths
        var requestedMode = mode ?? ConnectionMode.Direct;
        var options = CreateClientOptions(requestedMode);

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
                await this.CompleteTokenConnectionAndDisposeOnFailureAsync(client, vscCredential, vscProps.Id, subscriptionId, resourceGroupName, authorityHostUri, token);
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

            await this.CompleteTokenConnectionAndDisposeOnFailureAsync(client, credential, miProps.Id, subscriptionId, resourceGroupName, authorityHostUri, token);
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
                await this.CompleteTokenConnectionAndDisposeOnFailureAsync(client, browserCredential, entraProps.Id, subscriptionId, resourceGroupName, authorityHostUri, token);
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

                await this.CompleteTokenConnectionAndDisposeOnFailureAsync(client, deviceCodeCredential, dcProps.Id, subscriptionId, resourceGroupName, authorityHostUri, token);
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

            var dacCredential = new DefaultAzureCredential(dacOptions); // CodeQL [SM05137] Interactive developer CLI, not a hosted service: this is the last-resort fallback that adopts the developer's local identity (Azure CLI/azd, Visual Studio, env vars, or VM managed identity). No fixed service identity exists to pin to.
            client = new CosmosClient(endpoint, dacCredential, options);

            try
            {
                var dacProps = await ReadAccountAsync(client, token);
                await this.CompleteTokenConnectionAndDisposeOnFailureAsync(client, dacCredential, dacProps.Id, subscriptionId, resourceGroupName, authorityHostUri, token);
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

    internal static string GetLocalEmulatorConnectionFailureMessage(Uri endpoint)
    {
        var alternate = BuildAlternateEmulatorUri(endpoint);
        return MessageService.GetArgsString(
            "error-emulator_connection_failed",
            "endpoint",
            endpoint.ToString(),
            "alternate",
            alternate.ToString());
    }

    private static Uri BuildAlternateEmulatorUri(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint)
        {
            Scheme = string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? Uri.UriSchemeHttp
                : Uri.UriSchemeHttps,
        };
        return builder.Uri;
    }

    private static bool IsArmContextExplicitlyRequested(string? subscriptionId, string? resourceGroupName)
    {
        return !string.IsNullOrWhiteSpace(subscriptionId)
            || !string.IsNullOrWhiteSpace(resourceGroupName);
    }

    private async Task CompleteTokenConnectionAsync(
        CosmosClient client,
        TokenCredential credential,
        string accountId,
        string? subscriptionId,
        string? resourceGroupName,
        Uri? authorityHostUri,
        CancellationToken token)
    {
        var explicitlyRequested = IsArmContextExplicitlyRequested(subscriptionId, resourceGroupName);
        if (!explicitlyRequested)
        {
            this.Connect(client);
            WriteLine(MessageService.GetArgsString("command-connect-connected", "account", accountId));

            ArmCosmosContext? discoveredArmContext;
            try
            {
                discoveredArmContext = await this.TryDiscoverArmContextAsync(credential, client.Endpoint, subscriptionId, resourceGroupName, authorityHostUri, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }

            if (discoveredArmContext != null)
            {
                this.AttachArmContext(client, discoveredArmContext);
            }

            return;
        }

        var armContext = await this.TryDiscoverArmContextAsync(credential, client.Endpoint, subscriptionId, resourceGroupName, authorityHostUri, token);
        this.Connect(client, armContext);
        WriteLine(MessageService.GetArgsString("command-connect-connected", "account", accountId));
    }

    private async Task CompleteTokenConnectionAndDisposeOnFailureAsync(
        CosmosClient client,
        TokenCredential credential,
        string accountId,
        string? subscriptionId,
        string? resourceGroupName,
        Uri? authorityHostUri,
        CancellationToken token)
    {
        try
        {
            await this.CompleteTokenConnectionAsync(client, credential, accountId, subscriptionId, resourceGroupName, authorityHostUri, token);
        }
        catch
        {
            if (this.State is not ConnectedState connectedState || !ReferenceEquals(connectedState.Client, client))
            {
                client.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Wraps <see cref="CosmosArmResourceProvider.TryCreateContextAsync"/> so that an
    /// ARM discovery failure does not break a successful data-plane connection.
    /// When the user explicitly supplied <paramref name="subscriptionId"/> or
    /// <paramref name="resourceGroupName"/>, any failure bubbles up because the user
    /// explicitly requested ARM. Otherwise the failure is logged as a warning and
    /// discovery returns <c>null</c>; database and container commands continue through
    /// the data-plane resource strategy.
    /// </summary>
    private async Task<ArmCosmosContext?> TryDiscoverArmContextAsync(
        TokenCredential credential,
        Uri endpoint,
        string? subscriptionId,
        string? resourceGroupName,
        Uri? authorityHostUri,
        CancellationToken token)
    {
        var explicitlyRequested = IsArmContextExplicitlyRequested(subscriptionId, resourceGroupName);

        try
        {
            using var timeoutTokenSource = explicitlyRequested ? null : CancellationTokenSource.CreateLinkedTokenSource(token);
            if (timeoutTokenSource != null)
            {
                timeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(OptionalArmDiscoveryTimeoutSeconds));
            }

            return await CosmosArmResourceProvider.TryCreateContextAsync(
                credential,
                endpoint,
                subscriptionId,
                resourceGroupName,
                authorityHostUri,
                timeoutTokenSource?.Token ?? token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (ShellException) when (explicitlyRequested)
        {
            // Localized validation/cycle errors should always reach the user when
            // they explicitly opted into ARM via --subscription/--resource-group.
            throw;
        }
        catch (ShellException) when (!explicitlyRequested)
        {
            // Discovery succeeded enough to know there are multiple matching ARM
            // accounts. Without explicit coordinates we cannot pick one, but we
            // still tell the user how to disambiguate instead of silently falling
            // back as if ARM was simply unreachable.
            WriteLine(MessageService.GetString("shell-connect-arm-discovery-ambiguous"));
            return null;
        }
        catch (Exception) when (!explicitlyRequested)
        {
            WriteLine(MessageService.GetString("shell-connect-arm-discovery-failed"));
            return null;
        }
    }

    /// <summary>
    /// Connects to a client &amp; disposes old state.
    /// </summary>
    internal void Connect(CosmosClient client, ArmCosmosContext? armContext = null)
    {
        this.State?.Dispose();
        this.State = new ConnectedState(client, armContext);
        CosmosCompleteCommand.ClearDatabases();
        CosmosCompleteCommand.ClearContainers();
    }

    private void AttachArmContext(CosmosClient client, ArmCosmosContext armContext)
    {
        if (this.State is ConnectedState connectedState && ReferenceEquals(connectedState.Client, client))
        {
            this.State = new ConnectedState(client, armContext);
        }
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
                AnsiConsole.WriteException(e);
                return new ErrorCommandState(e);
            }

            // Operational exceptions (Ctrl+C, SDK failures, our own shell/command
            // exceptions) carry an actionable message; the stack trace is noise
            // for end users. Show only Message chains and let --verbose surface
            // the full exception.
            if (e is OperationCanceledException)
            {
                var canceled = MessageService.GetString("runtime-error-canceled");
                if (!string.IsNullOrEmpty(canceled))
                {
                    AnsiConsole.MarkupLine(Theme.FormatWarning(canceled));
                }

                return new ErrorCommandState(e);
            }

            var prefix = MessageService.GetString("runtime-error-prefix") ?? "error";
            AnsiConsole.MarkupLine($"{Theme.FormatError(prefix + ":")} {Markup.Escape(e.Message)}");
            if (e is IShellExceptionWithHint hinted && !string.IsNullOrEmpty(hinted.Hint))
            {
                AnsiConsole.MarkupLine(Markup.Escape(hinted.Hint));
            }

            var inner = e.InnerException;
            while (inner != null)
            {
                AnsiConsole.MarkupLine($"  {Theme.FormatError("\u2192")} {Markup.Escape(inner.Message)}");
                inner = inner.InnerException;
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
        Console.Write(CosmosShellPrompt.PromptMarker + " ");
        return Console.ReadLine();
    }

    private static CosmosClientOptions CreateClientOptions(ConnectionMode requestedMode)
    {
        var options = new CosmosClientOptions
        {
            ApplicationName = "CosmosDBShell",
            ConnectionMode = requestedMode,
            CosmosClientTelemetryOptions = new CosmosClientTelemetryOptions
            {
                DisableDistributedTracing = false,
            },
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions()
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            },
        };

        return options;
    }

    private LineEditor CreateLineEditor()
    {
        try
        {
            this.cosmosShellPrompt = new CosmosShellPrompt(this);
            var lineEditor = new LineEditor()
            {
                Prompt = this.cosmosShellPrompt,
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
        this.pendingMultiLineBuffer = null;
        this.pendingMultiLineSuppressesNewline = false;
        if (this.cosmosShellPrompt != null)
        {
            this.cosmosShellPrompt.InContinuation = false;
        }

        this.CancelPrompt();
        WriteLine("̂C");
    }

    private void SaveHistory()
    {
        if (this.history.Count > MAXHISTORYITEMS)
        {
            this.history = [.. this.history.Skip(this.history.Count - MAXHISTORYITEMS)];
        }

        File.WriteAllLines(this.HistoryFile, this.history.Select(EncodeHistoryLine));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "History helpers are grouped with SaveHistory for cohesion.")]
    internal static string EncodeHistoryLine(string line)
    {
        if (line.IndexOfAny(['\n', '\r']) < 0 && !line.StartsWith(EncodedHistoryLinePrefix, StringComparison.Ordinal))
        {
            return line;
        }

        var sb = new System.Text.StringBuilder(line.Length + 8);
        foreach (var ch in line)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(ch); break;
            }
        }

        return EncodedHistoryLinePrefix + EncodedHistoryLineMarker + sb.ToString();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "History helpers are grouped with SaveHistory for cohesion.")]
    internal static string DecodeHistoryLine(string line)
    {
        // Require both the prefix and the encoder-only marker so a user command
        // that happens to start with the prefix string is never silently rewritten.
        if (!line.StartsWith(EncodedHistoryLinePrefix + EncodedHistoryLineMarker, StringComparison.Ordinal))
        {
            return line;
        }

        var payload = line.Substring(EncodedHistoryLinePrefix.Length + EncodedHistoryLineMarker.Length);

        // Defensive: validate that the payload only contains escape sequences
        // we emit (\\, \n, \r). If a line was hand-edited and broke the format,
        // fall back to returning it untouched rather than mangling the data.
        for (int i = 0; i < payload.Length; i++)
        {
            if (payload[i] != '\\')
            {
                continue;
            }

            if (i + 1 >= payload.Length)
            {
                return line;
            }

            var next = payload[i + 1];
            if (next != '\\' && next != 'n' && next != 'r')
            {
                return line;
            }

            i++;
        }

        var sb = new System.Text.StringBuilder(payload.Length);
        for (int i = 0; i < payload.Length; i++)
        {
            var ch = payload[i];
            if (ch == '\\' && i + 1 < payload.Length)
            {
                var next = payload[i + 1];
                switch (next)
                {
                    case '\\': sb.Append('\\'); i++; continue;
                    case 'n': sb.Append('\n'); i++; continue;
                    case 'r': sb.Append('\r'); i++; continue;
                }
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "Grouped with REPL helpers.")]
    internal static bool TryRemoveLineContinuation(ref string line)
    {
        if (line.Length == 0 || line[^1] != '\\')
        {
            return false;
        }

        int trailing = 0;
        for (int i = line.Length - 1; i >= 0 && line[i] == '\\'; i--)
        {
            trailing++;
        }

        if ((trailing & 1) == 0)
        {
            return false;
        }

        line = line.Substring(0, line.Length - 1);
        return true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "Grouped with REPL helpers.")]
    internal static void AppendMultiLineFragment(System.Text.StringBuilder buffer, string line, bool suppressNewline)
    {
        if (!suppressNewline)
        {
            buffer.Append('\n');
        }

        buffer.Append(line);
    }

    /// <summary>
    /// Processes one physical input line through the REPL multi-line accumulation state
    /// machine. Returns the joined command text when execution should proceed, or
    /// <c>null</c> when the loop should keep reading additional continuation lines (or
    /// when the input itself was cancelled and the pending buffer was discarded).
    /// </summary>
    /// <param name="input">The raw line returned by ReadLine, or <c>null</c> if ReadLine was cancelled.</param>
    /// <param name="pendingBuffer">The in-flight multi-line buffer; replaced or cleared in place.</param>
    /// <param name="pendingSuppressesNewline">Tracks whether the next appended fragment must splice without a newline (backslash continuation).</param>
    /// <param name="prompt">Optional prompt whose <c>InContinuation</c> flag is kept in sync; may be <c>null</c> in non-interactive callers and tests.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "Grouped with REPL helpers.")]
    internal static string? ProcessInteractiveLine(
        string? input,
        ref System.Text.StringBuilder? pendingBuffer,
        ref bool pendingSuppressesNewline,
        CosmosShellPrompt? prompt = null)
    {
        if (input is not { } line)
        {
            // ReadLine cancelled (Ctrl+C). Discard any in-progress multi-line buffer.
            pendingBuffer = null;
            pendingSuppressesNewline = false;
            if (prompt != null)
            {
                prompt.InContinuation = false;
            }

            return null;
        }

        // Detect explicit backslash-at-end-of-line continuation (bash-style).
        bool backslashContinuation = TryRemoveLineContinuation(ref line);

        // Compute the "incomplete?" decision exactly once per Enter press: parsing is
        // not free, and the previous shape evaluated the same text twice on the line
        // that starts a multi-line buffer.
        bool incompleteAggregated;
        if (pendingBuffer != null)
        {
            AppendMultiLineFragment(pendingBuffer, line, pendingSuppressesNewline);
            incompleteAggregated = backslashContinuation || IsIncompleteInput(pendingBuffer.ToString());
        }
        else
        {
            bool lineIncomplete = backslashContinuation || IsIncompleteInput(line);
            if (!lineIncomplete)
            {
                return line;
            }

            pendingBuffer = new System.Text.StringBuilder(line);
            incompleteAggregated = true; // aggregated == line on the first iteration
        }

        if (incompleteAggregated)
        {
            if (prompt != null)
            {
                prompt.InContinuation = true;
            }

            pendingSuppressesNewline = backslashContinuation;
            return null;
        }

        var aggregated = pendingBuffer.ToString();
        pendingBuffer = null;
        pendingSuppressesNewline = false;
        if (prompt != null)
        {
            prompt.InContinuation = false;
        }

        return aggregated;
    }

    /// <summary>
    /// Returns true if the given input text appears to be an incomplete shell command —
    /// either because the lexer flagged an unterminated string or the parser ran off the
    /// end of input. Used by the REPL to decide whether to prompt for a continuation line.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "Grouped with REPL helpers.")]
    internal static bool IsIncompleteInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var lexer = new Lexer(text);
            var parser = new StatementParser(lexer);

            // ParseStatements() runs the parser to end-of-input and returns the
            // full statement list eagerly; the result is discarded because we
            // only care about whether parsing flagged the input as incomplete.
            _ = parser.ParseStatements();

            foreach (var err in parser.Errors)
            {
                if (err.ErrorLevel != ErrorLevel.Error)
                {
                    continue;
                }

                if (err.Kind == ParseErrorKind.UnexpectedEnd || err.Kind == ParseErrorKind.UnterminatedString)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ReportExecutionError(Exception e, string? sourceText = null)
    {
        // The command already emitted a friendly diagnostic; do not print again.
        if (ContainsException<CommandReportedException>(e))
        {
            return;
        }

        if (e is PositionalException pe)
        {
            this.ReportPositionalError(pe);
            return;
        }

        // Unknown-command errors carry the offset of the offending name in the
        // typed line. Render them with the same compiler-style caret used for
        // parser errors so the user can see exactly which fragment failed (for
        // example the trailing piece of an unquoted connection string).
        if (e is CommandNotFoundException cnf && cnf.Start.HasValue && !string.IsNullOrEmpty(sourceText))
        {
            this.ReportCommandNotFoundError(cnf, sourceText!);
            return;
        }

        var prefix = e is CommandException ce ? $"{ce.Command}: " : string.Empty;
        var showInner = e is not ShellException && e.InnerException != null;
        var hint = e is IShellExceptionWithHint hinted ? hinted.Hint : null;

        if (this.ErrOutRedirect != null)
        {
            var errTxt = this.Options?.Verbose == true
                ? e.ToString()
                : prefix + e.Message
                    + (!string.IsNullOrEmpty(hint) ? Environment.NewLine + hint : string.Empty)
                    + (showInner ? Environment.NewLine + FormatInnerExceptionMessages(e.InnerException) : string.Empty);
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
            AnsiConsole.MarkupLine(prefix + Theme.FormatError(e.Message));
            if (!string.IsNullOrEmpty(hint))
            {
                AnsiConsole.MarkupLine(Markup.Escape(hint));
            }

            if (showInner)
            {
                var inner = e.InnerException;
                while (inner != null)
                {
                    AnsiConsole.MarkupLine($"  {Theme.FormatError("->")} {Markup.Escape(inner.Message)}");
                    inner = inner.InnerException;
                }
            }
        }
    }

    private void ReportCommandNotFoundError(CommandNotFoundException e, string sourceText)
    {
        var lines = this.SplitIntoLines(sourceText);
        var (lineIndex, column) = this.OffsetToLineColumn(sourceText, e.Start!.Value);
        var lineNumber = lineIndex + 1;
        var rawLineText = lineIndex >= 0 && lineIndex < lines.Length ? lines[lineIndex] : string.Empty;
        var rendered = SourceCaretRenderer.Render(rawLineText, column + 1, e.Length is > 0 ? e.Length.Value : 1);

        var prefix = MessageService.GetString("runtime-error-prefix") ?? "error";
        var fileBuffer = this.ErrOutRedirect != null ? new System.Text.StringBuilder() : null;

        this.AppendSourceCaretDiagnostic(
            fileBuffer,
            prefix,
            isWarning: false,
            e.Message,
            lineNumber,
            rendered,
            origin: this.GetDiagnosticOrigin(this.CurrentScriptFileName));

        if (!string.IsNullOrEmpty(e.Hint))
        {
            if (fileBuffer != null)
            {
                fileBuffer.Append(e.Hint).Append(Environment.NewLine);
            }
            else
            {
                AnsiConsole.MarkupLine(Markup.Escape(e.Hint));
            }
        }

        // Emitted in addition to any "Did you mean" hint: a connection-string
        // fragment can still match a known command, and the quoting guidance is
        // the real fix the user needs.
        if (LooksLikeConnectionStringLine(rawLineText))
        {
            // An unquoted connection string is split on ';' into fragments that
            // surface as unknown commands. Point the user at the real fix.
            var csHint = MessageService.GetString("error-command-not-found-connection-string");
            if (!string.IsNullOrEmpty(csHint))
            {
                if (fileBuffer != null)
                {
                    fileBuffer.Append(csHint).Append(Environment.NewLine);
                }
                else
                {
                    AnsiConsole.MarkupLine(Markup.Escape(csHint));
                }
            }
        }

        if (fileBuffer != null)
        {
            var payload = fileBuffer.ToString();
            if (this.AppendErrRedirection)
            {
                File.AppendAllText(this.ErrOutRedirect!, payload);
            }
            else
            {
                File.WriteAllText(this.ErrOutRedirect!, payload);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "Diagnostic helpers are grouped with diagnostic rendering code.")]
    private static bool LooksLikeConnectionStringLine(string lineText)
    {
        if (string.IsNullOrEmpty(lineText))
        {
            return false;
        }

        return lineText.Contains("AccountEndpoint=", StringComparison.OrdinalIgnoreCase)
            || lineText.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase);
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
            AnsiConsole.MarkupLine($"{Markup.Escape($"{pe.FileName}:{pe.Line}:{pe.Column}:")} {Theme.FormatError("error:")} {Markup.Escape(pe.Message)}");
            if (pe.LineText != null)
            {
                AnsiConsole.MarkupLine("  " + Theme.FormatMuted(pe.LineText));
                AnsiConsole.MarkupLine("  " + Theme.FormatError(new string(' ', Math.Max(0, pe.Column - 1)) + "^"));
            }
        }
    }

    private string[] SplitIntoLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new[] { string.Empty };
        }

        // Split on \n and strip trailing \r so the displayed source line
        // does not include carriage returns from CRLF inputs.
        var raw = text.Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i].EndsWith('\r'))
            {
                raw[i] = raw[i][..^1];
            }
        }

        return raw;
    }

    private (int Line, int Column) OffsetToLineColumn(string text, int absolute)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        absolute = Math.Clamp(absolute, 0, text.Length);
        int line = 0;
        int lastNl = -1;
        for (int i = 0; i < absolute; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastNl = i;
            }
        }

        int column = absolute - (lastNl + 1);
        return (line, column);
    }

    private void ReportParserErrors(ErrorList errors, string commandText)
    {
        if (errors == null || errors.Count == 0)
        {
            return;
        }

        var lines = this.SplitIntoLines(commandText ?? string.Empty);
        var redirected = this.ErrOutRedirect != null;
        var fileBuffer = redirected ? new System.Text.StringBuilder() : null;

        // Show every warning, but only the first hard error. Later errors are
        // almost always recovery cascades from the first one and only add noise
        // in an interactive shell.
        var reportedError = false;
        foreach (var error in errors)
        {
            if (error == null)
            {
                continue;
            }

            var isWarning = error.ErrorLevel == ErrorLevel.Warning;
            if (!isWarning)
            {
                if (reportedError)
                {
                    continue;
                }

                reportedError = true;
            }

            var (lineIndex, column) = this.OffsetToLineColumn(commandText ?? string.Empty, error.Start);
            var lineNumber = lineIndex + 1;
            var rawLineText = lineIndex >= 0 && lineIndex < lines.Length ? lines[lineIndex] : string.Empty;
            var rendered = SourceCaretRenderer.Render(rawLineText, column + 1);

            var levelPrefix = MessageService.GetString(isWarning ? "parser-warning-prefix" : "parser-error-prefix");

            this.AppendSourceCaretDiagnostic(
                fileBuffer,
                levelPrefix,
                isWarning,
                error.Message,
                lineNumber,
                rendered,
                origin: this.GetDiagnosticOrigin(this.CurrentScriptFileName));
        }

        if (redirected && fileBuffer != null)
        {
            var payload = fileBuffer.ToString();
            if (this.AppendErrRedirection)
            {
                File.AppendAllText(this.ErrOutRedirect!, payload);
            }
            else
            {
                File.WriteAllText(this.ErrOutRedirect!, payload);
            }
        }
    }

    /// <summary>
    /// Attempts to render a Cosmos NoSQL query error in the same compiler-style
    /// format used for parser errors. Returns true when the error message
    /// contained a recognisable source location and the diagnostic was
    /// written; the caller can then throw a CommandReportedException so the
    /// generic error reporter stays silent.
    /// </summary>
    internal bool TryReportQueryError(string query, string rawMessage)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(rawMessage))
        {
            return false;
        }

        var location = QueryErrorLocator.TryLocate(query, rawMessage);
        if (location == null)
        {
            return false;
        }

        var lines = this.SplitIntoLines(query);
        var lineIndex = Math.Clamp(location.Line - 1, 0, Math.Max(0, lines.Length - 1));
        var rawLineText = lines.Length > 0 ? lines[lineIndex] : string.Empty;
        var rendered = SourceCaretRenderer.Render(rawLineText, location.Column, location.Length);

        var prefix = MessageService.GetString("query-error-prefix");
        var message = location.Message ?? rawMessage;

        System.Text.StringBuilder? fileBuffer = this.ErrOutRedirect != null ? new System.Text.StringBuilder() : null;
        this.AppendSourceCaretDiagnostic(
            fileBuffer,
            prefix,
            isWarning: false,
            message,
            location.Line,
            rendered,
            origin: this.GetDiagnosticOrigin(this.CurrentScriptFileName));

        if (fileBuffer != null)
        {
            var payload = fileBuffer.ToString();
            if (this.AppendErrRedirection)
            {
                File.AppendAllText(this.ErrOutRedirect!, payload);
            }
            else
            {
                File.WriteAllText(this.ErrOutRedirect!, payload);
            }
        }

        return true;
    }

    private void AppendSourceCaretDiagnostic(
        System.Text.StringBuilder? fileBuffer,
        string levelPrefix,
        bool isWarning,
        string message,
        int lineNumber,
        RenderedSourceCaret rendered,
        string? origin = null)
    {
        string FormatLevel(string text) => isWarning ? Theme.FormatWarning(text) : Theme.FormatError(text);

        // When the diagnostic originates from a script file we prepend the
        // "file:line:col:" prefix in front of the level prefix (cargo / clang
        // style) so editors and humans can jump straight to the offending
        // line. Interactive prompt errors keep the trailing " (L:C)" form so
        // they read naturally without a fake file name.
        var hasOrigin = !string.IsNullOrEmpty(origin);
        if (fileBuffer != null)
        {
            if (hasOrigin)
            {
                fileBuffer.Append(origin).Append(':').Append(lineNumber).Append(':').Append(rendered.SourceColumn).Append(": ")
                    .Append(levelPrefix).Append(": ").Append(message).Append(Environment.NewLine);
            }
            else
            {
                fileBuffer.Append(levelPrefix).Append(": ").Append(message)
                    .Append(" (").Append(lineNumber).Append(':').Append(rendered.SourceColumn).Append(')')
                    .Append(Environment.NewLine);
            }

            var gutter = $"  > {lineNumber} | ";
            fileBuffer.Append(gutter).Append(rendered.Display).Append(Environment.NewLine);
            fileBuffer.Append(new string(' ', gutter.Length))
                .Append(rendered.CaretLeader)
                .Append(rendered.CaretPad)
                .Append(rendered.CaretMarker)
                .Append(Environment.NewLine);
        }
        else
        {
            var m = Markup.Escape(message);
            if (hasOrigin)
            {
                var location = $"{origin}:{lineNumber}:{rendered.SourceColumn}:";
                AnsiConsole.MarkupLine($"{Theme.FormatMuted(location)} {FormatLevel(levelPrefix + ":")} {m}");
            }
            else
            {
                AnsiConsole.MarkupLine($"{FormatLevel(levelPrefix + ":")} {m} {Theme.FormatMuted($"({lineNumber}:{rendered.SourceColumn})")}");
            }

            var gutter = $"  > {lineNumber} | ";
            AnsiConsole.MarkupLine($"{Theme.FormatMuted(gutter)}{Markup.Escape(rendered.Display)}");
            AnsiConsole.MarkupLine($"{Theme.FormatMuted(new string(' ', gutter.Length) + rendered.CaretLeader)}{FormatLevel(rendered.CaretPad + rendered.CaretMarker)}");
        }
    }

    private string? GetDiagnosticOrigin(string? scriptFileName)
    {
        if (string.IsNullOrEmpty(scriptFileName))
        {
            return null;
        }

        // Show just the file name so absolute paths don't leak into the
        // diagnostic and so output stays terse in CI logs.
        try
        {
            var leaf = scriptFileName
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            return string.IsNullOrEmpty(leaf) ? scriptFileName : leaf;
        }
        catch (ArgumentException)
        {
            return scriptFileName;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "Diagnostic helpers are grouped with diagnostic rendering code.")]
    private static bool ContainsException<TException>(Exception? exception)
        where TException : Exception
    {
        while (exception != null)
        {
            if (exception is TException)
            {
                return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204", Justification = "Diagnostic helpers are grouped with diagnostic rendering code.")]
    private static string FormatInnerExceptionMessages(Exception? exception)
    {
        var sb = new System.Text.StringBuilder();
        var first = true;
        while (exception != null)
        {
            if (!first)
            {
                sb.Append(Environment.NewLine);
            }

            sb.Append(exception.Message);
            first = false;
            exception = exception.InnerException;
        }

        return sb.ToString();
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
                AnsiConsole.Markup(Theme.FormatError("Error:"));
                ShellInterpreter.Instance.WriteLine($"{cmdStr} not found.");
            }
            ShellInterpreter.Instance.WriteLine("```");
        }
        */
}
