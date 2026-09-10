namespace Azure.Data.Cosmos.Shell.Commands;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using Spectre.Console;

[CosmosCommand("doctor")]
[CosmosExample("doctor", Description = "Check the local environment and current connection")]
[CosmosExample("doctor who", Description = "Include the known identity and access context without acquiring a token")]
[CosmosExample("doctor --database MyDb --container Items --query --format json", Description = "Check a container and run a bounded query probe")]
[McpAnnotation(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
internal sealed class DoctorCommand : CosmosCommand
{
    [CosmosParameter("subcommand", IsRequired = false)]
    public string? Subcommand { get; init; }

    [CosmosOption("who")]
    public bool Who { get; init; }

    private bool IncludeIdentity => this.Who || this.Subcommand == "who";

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("format", "f")]
    public string? Format { get; init; }

    [CosmosOption("query")]
    public bool Query { get; init; }

    [CosmosOption("arm")]
    public bool Arm { get; init; }

    [CosmosOption("timeout")]
    public int Timeout { get; init; } = 20;

    internal Func<string, CancellationToken, Task> ResolveHostAsync { get; init; } = async (host, token) =>
        await Dns.GetHostAddressesAsync(host, token);

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        commandState.SetFormat(this.Format);
        if (this.Subcommand != null && this.Subcommand != "who")
        {
            throw new CommandException("doctor", Message("invalid-subcommand"));
        }

        if (this.Timeout is < 1 or > 120)
        {
            throw new CommandException("doctor", Message("invalid-timeout"));
        }

        var (database, container) = this.ResolveTarget(shell.State);
        if (container != null && database == null)
        {
            throw new CommandException("doctor", Message("container-without-database"));
        }

        if (this.Query && container == null)
        {
            throw new CommandException("doctor", Message("query-without-container"));
        }

        var checks = new List<DoctorCheck>
        {
            new("shell", "PASS", typeof(ShellInterpreter).Assembly.GetName().Version?.ToString() ?? "unknown"),
            new("runtime", "PASS", RuntimeInformation.FrameworkDescription),
            new("platform", "PASS", $"{Environment.OSVersion.Platform} / {RuntimeInformation.ProcessArchitecture}"),
            Check("installation", "SKIP", "installation-unknown"),
            Check("proxy", "PASS", HasProxyConfiguration() ? "proxy-configured" : "proxy-not-configured"),
        };

        if (this.IncludeIdentity)
        {
            var credentialType = shell.ActiveCredentialType;
            var knownCredential = credentialType is "AzureCliCredential" or "ManagedIdentityCredential" or "StaticTokenCredential"
                or "EnvironmentCredential" or "WorkloadIdentityCredential" or "ClientSecretCredential" or "ClientCertificateCredential"
                or "DefaultAzureCredential" or "InteractiveBrowserCredential" or "DeviceCodeCredential" or "VisualStudioCodeCredential"
                or "AccountKey" or "Emulator";
            var isConnected = shell.State is ConnectedState;
            checks.Add(isConnected && knownCredential
                ? Check("identity", "PASS", "identity-configured") with { Message = $"{Message("identity-configured")} {credentialType}", CredentialType = credentialType }
                : Check("identity", "SKIP", isConnected ? "identity-unknown" : "not-connected"));
            checks.Add(Check("scope", isConnected ? "PASS" : "SKIP", !isConnected ? "not-connected" : container != null ? "scope-container" : database != null ? "scope-database" : "scope-account"));
            checks.Add(Check("write-access", "SKIP", "write-not-assessed"));
        }

        if (shell.State is not ConnectedState connected)
        {
            checks.Add(Check("connection", this.Database != null || this.Container != null || this.Query ? "FAIL" : "SKIP", "not-connected"));
            checks.Add(Check("arm", this.Arm ? "FAIL" : "SKIP", "not-connected"));
            return this.CreateResult(checks, commandState, shell.Options?.Quiet == true);
        }

        checks.Add(Check("connection", "PASS", connected.Client.ClientOptions.ConnectionMode == ConnectionMode.Direct ? "direct-configured" : "gateway-configured"));
        var canProbeCredential = !MayPrompt(shell.ActiveCredential?.GetType().Name);
        checks.Add(Check("authentication", canProbeCredential ? "PASS" : this.Query || this.Arm || this.Database != null || this.Container != null ? "FAIL" : "WARN", canProbeCredential ? "authentication-configured" : "interactive-credential"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(this.Timeout));

        var dns = await RunCheckAsync(
            "dns",
            async checkToken =>
            {
                await this.ResolveHostAsync(connected.Client.Endpoint.DnsSafeHost, checkToken);
                return null;
            },
            "dns-resolved",
            true,
            TimeSpan.FromSeconds(5),
            deadline.Token,
            token);
        checks.Add(dns);

        if (dns.Status == "PASS" && canProbeCredential)
        {
            checks.Add(await RunCheckAsync(
                "access",
                async checkToken =>
                {
                    if (container != null)
                    {
                        var response = await connected.Client.GetContainer(database!, container).ReadContainerAsync(cancellationToken: checkToken);
                        return response.RequestCharge;
                    }

                    if (database != null)
                    {
                        var response = await connected.Client.GetDatabase(database).ReadAsync(cancellationToken: checkToken);
                        return response.RequestCharge;
                    }

                    await connected.Client.ReadAccountAsync().WaitAsync(checkToken);
                    return null;
                },
                container != null ? "container-readable" : database != null ? "database-readable" : "account-readable",
                true,
                TimeSpan.FromSeconds(5),
                deadline.Token,
                token));
        }
        else
        {
            checks.Add(Check("access", "SKIP", "dependency-failed"));
        }

        if (this.Query && checks.Last().Status == "PASS")
        {
            checks.Add(await RunCheckAsync(
                "query",
                async checkToken =>
                {
                    using var iterator = connected.Client.GetContainer(database!, container!).GetItemQueryStreamIterator(
                        "SELECT TOP 1 VALUE 1 FROM c",
                        requestOptions: new QueryRequestOptions { MaxItemCount = 1, MaxConcurrency = 1, MaxBufferedItemCount = 1 });
                    using var response = await iterator.ReadNextAsync(checkToken);
                    response.EnsureSuccessStatusCode();
                    return response.Headers.RequestCharge;
                },
                "query-readable",
                true,
                TimeSpan.FromSeconds(5),
                deadline.Token,
                token));
        }
        else
        {
            checks.Add(Check("query", "SKIP", this.Query ? "dependency-failed" : "query-not-requested"));
        }

        if (!canProbeCredential && (connected.ArmContext != null || this.Arm))
        {
            checks.Add(Check("arm", this.Arm ? "FAIL" : "SKIP", "interactive-credential"));
        }
        else if (connected.ArmContext != null || (this.Arm && shell.ActiveCredential != null))
        {
            checks.Add(await RunCheckAsync(
                "arm",
                async checkToken =>
                {
                    var context = connected.ArmContext ?? await CosmosArmResourceProvider.TryCreateContextAsync(
                        shell.ActiveCredential, connected.Client.Endpoint, null, null, null, checkToken);
                    if (context == null)
                    {
                        throw new InvalidOperationException();
                    }

                    await context.Account.GetAsync(checkToken);
                    return null;
                },
                "arm-readable",
                this.Arm,
                TimeSpan.FromSeconds(5),
                deadline.Token,
                token));
        }
        else
        {
            checks.Add(Check("arm", this.Arm ? "FAIL" : "SKIP", this.Arm ? "arm-credential-required" : "arm-unavailable"));
        }

        token.ThrowIfCancellationRequested();
        return this.CreateResult(checks, commandState, shell.Options?.Quiet == true);
    }

    internal static async Task<DoctorCheck> RunCheckAsync(
        string id,
        Func<CancellationToken, Task<double?>> probe,
        string successCode,
        bool required,
        TimeSpan timeout,
        CancellationToken deadline,
        CancellationToken token)
    {
        using var checkCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline, token);
        checkCancellation.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            checkCancellation.Token.ThrowIfCancellationRequested();
            var charge = await probe(checkCancellation.Token).WaitAsync(checkCancellation.Token);
            return Check(id, "PASS", successCode) with { DurationMs = stopwatch.ElapsedMilliseconds, RequestCharge = charge };
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            var code = ClassifyFailure(exception);
            return Check(id, required ? "FAIL" : "WARN", code) with
            {
                DurationMs = stopwatch.ElapsedMilliseconds,
                RequestCharge = exception is CosmosException cosmos ? cosmos.RequestCharge : null,
            };
        }
    }

    internal static string ClassifyFailure(Exception exception)
    {
        var status = exception switch
        {
            CosmosException cosmos => (int)cosmos.StatusCode,
            global::Azure.RequestFailedException azure => azure.Status,
            _ => 0,
        };

        return status switch
        {
            401 => "unauthorized",
            403 => "forbidden",
            404 => "not-found",
            429 => "throttled",
            408 or 503 => "unreachable",
            _ => exception switch
            {
                OperationCanceledException or TimeoutException => "timeout",
                SocketException => "dns-failed",
                AuthenticationException => "tls-failed",
                HttpRequestException => "unreachable",
                _ => "probe-failed",
            },
        };
    }

    internal static bool MayPrompt(string? credentialType) => credentialType is not
        (null or "AzureCliCredential" or "ManagedIdentityCredential" or "StaticTokenCredential" or "EnvironmentCredential" or "WorkloadIdentityCredential" or "ClientSecretCredential" or "ClientCertificateCredential");

    internal (string? Database, string? Container) ResolveTarget(State state)
    {
        var database = this.Database ?? (state as DatabaseState)?.DatabaseName;
        var container = this.Container ?? (this.Database == null ? (state as ContainerState)?.ContainerName : null);
        return (database, container);
    }

    private static string Message(string code) => MessageService.GetString($"command-doctor-{code}");

    private static DoctorCheck Check(string id, string status, string code) => new(id, status, Message(code), code);

    private static bool HasProxyConfiguration() => new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" }
        .Any(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)));

    internal CommandState CreateResult(List<DoctorCheck> checks, CommandState commandState, bool quiet = false)
    {
        var result = new DoctorCommandState(checks.Any(check => check.Status == "FAIL"));
        if (commandState.OutputFormatExplicitlySet)
        {
            result.OutputFormat = commandState.OutputFormat;
        }

        result.SetFormat(this.Format);
        result.RequestCharge = checks.Any(check => check.RequestCharge.HasValue) ? checks.Sum(check => check.RequestCharge ?? 0) : null;
        result.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            status = result.IsError ? "FAIL" : checks.Any(check => check.Status == "WARN") ? "WARN" : "PASS",
            checks = checks.Select(check => new { id = check.Id, status = check.Status, code = check.Code, message = check.Message, durationMs = check.DurationMs, requestCharge = check.RequestCharge, credentialType = check.CredentialType }),
        }));
        result.RenderUser = () =>
        {
            if (quiet)
            {
                return;
            }

            if (this.IncludeIdentity)
            {
                AnsiConsole.MarkupLine(Theme.FormatSectionHeader(Message("who-heading")));
            }

            foreach (var check in checks)
            {
                AnsiConsole.MarkupLine($"{FormatStatus(check.Status)}  {Theme.FormatHelpName(check.Id.PadRight(18))} {FormatCheckMessage(check)}");
            }
        };
        return result;
    }

    private static string FormatStatus(string status)
    {
        var padded = status.PadRight(4);
        return status switch
        {
            "PASS" => Theme.FormatSuccess(padded),
            "WARN" => Theme.FormatWarning(padded),
            "FAIL" => Theme.FormatError(padded),
            _ => Theme.FormatMuted(padded),
        };
    }

    private static string FormatCheckMessage(DoctorCheck check) =>
        check.Status == "SKIP" ? Theme.FormatMuted(check.Message) : Markup.Escape(check.Message);

    internal sealed record DoctorCheck(string Id, string Status, string Message, string Code = "available", long DurationMs = 0, double? RequestCharge = null)
    {
        public string? CredentialType { get; init; }
    }

    private sealed class DoctorCommandState(bool failed) : CommandState
    {
        public override bool IsError => failed;

        public override int ExitCode => failed ? 1 : 0;
    }
}