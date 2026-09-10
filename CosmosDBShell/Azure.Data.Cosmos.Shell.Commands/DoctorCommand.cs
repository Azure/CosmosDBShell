namespace Azure.Data.Cosmos.Shell.Commands;

using System.Diagnostics;
using System.Globalization;
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

    [CosmosOption("no-update-check")]
    public bool NoUpdateCheck { get; init; }

    [CosmosOption("timeout")]
    public int Timeout { get; init; } = 20;

    internal Func<string, CancellationToken, Task> ResolveHostAsync { get; init; } = async (host, token) =>
        await Dns.GetHostAddressesAsync(host, token);

    internal Func<CancellationToken, Task<JsonElement>> FetchReleasesAsync { get; init; } = ShellUpdateChecker.FetchReleasesAsync;

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
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

        var currentVersion = ShellInterpreter.GetDisplayVersion(typeof(ShellInterpreter).Assembly);
        var checks = new List<DoctorCheck>
        {
            new("shell", "PASS", currentVersion),
            new("runtime", "PASS", RuntimeInformation.FrameworkDescription),
            new("platform", "PASS", $"{Environment.OSVersion.Platform} / {RuntimeInformation.ProcessArchitecture}"),
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

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(this.Timeout));
        if (shell.State is not ConnectedState connected)
        {
            checks.Add(Check("connection", this.Database != null || this.Container != null || this.Query ? "FAIL" : "SKIP", "not-connected"));
            checks.Add(Check("arm", this.Arm ? "FAIL" : "SKIP", "not-connected"));
            checks.Add(CreateClockCheck(checks));
            checks.Add(await this.CheckUpdatesAsync(currentVersion, deadline.Token, token));
            return this.CreateResult(checks, commandState, shell.Options?.Quiet == true, stopwatch.ElapsedMilliseconds);
        }

        checks.Add(Check("connection", "PASS", connected.Client.ClientOptions.ConnectionMode == ConnectionMode.Direct ? "direct-configured" : "gateway-configured"));
        var canProbeCredential = !MayPrompt(shell.ActiveCredential?.GetType().Name);
        checks.Add(Check("authentication", canProbeCredential ? "PASS" : this.Query || this.Arm || this.Database != null || this.Container != null ? "FAIL" : "WARN", canProbeCredential ? "authentication-configured" : "interactive-credential"));

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

        DoctorCheck access;
        if (dns.Status == "PASS" && canProbeCredential)
        {
            access = await RunResponseCheckAsync(
                "access",
                async checkToken =>
                {
                    if (container != null)
                    {
                        var response = await connected.Client.GetContainer(database!, container).ReadContainerAsync(cancellationToken: checkToken);
                        return new ProbeResponse(response.RequestCharge, response.Headers?["Date"]);
                    }

                    if (database != null)
                    {
                        var response = await connected.Client.GetDatabase(database).ReadAsync(cancellationToken: checkToken);
                        return new ProbeResponse(response.RequestCharge, response.Headers?["Date"]);
                    }

                    await connected.Client.ReadAccountAsync().WaitAsync(checkToken);
                    return new ProbeResponse(null, null);
                },
                container != null ? "container-readable" : database != null ? "database-readable" : "account-readable",
                true,
                TimeSpan.FromSeconds(5),
                deadline.Token,
                token);
        }
        else
        {
            access = Check("access", "SKIP", !canProbeCredential ? "interactive-credential" : "dependency-failed");
        }

        checks.Add(access);
        if (this.Query && access.Status == "PASS")
        {
            checks.Add(await RunResponseCheckAsync(
                "query",
                async checkToken =>
                {
                    using var iterator = connected.Client.GetContainer(database!, container!).GetItemQueryStreamIterator(
                        "SELECT TOP 1 VALUE 1 FROM c",
                        requestOptions: new QueryRequestOptions { MaxItemCount = 1, MaxConcurrency = 1, MaxBufferedItemCount = 1 });
                    using var response = await iterator.ReadNextAsync(checkToken);
                    response.EnsureSuccessStatusCode();
                    return new ProbeResponse(response.Headers.RequestCharge, response.Headers["Date"]);
                },
                "query-readable",
                true,
                TimeSpan.FromSeconds(5),
                deadline.Token,
                token));
        }
        else
        {
            checks.Add(Check("query", "SKIP", !this.Query ? "query-not-requested" : !canProbeCredential ? "interactive-credential" : "dependency-failed"));
        }

        if (!canProbeCredential && (connected.ArmContext != null || this.Arm))
        {
            checks.Add(Check("arm", this.Arm ? "FAIL" : "SKIP", "interactive-credential"));
        }
        else if (connected.ArmContext != null || (this.Arm && shell.ActiveCredential != null))
        {
            checks.Add(await RunResponseCheckAsync(
                "arm",
                async checkToken =>
                {
                    var context = connected.ArmContext ?? await CosmosArmResourceProvider.TryCreateContextAsync(
                        shell.ActiveCredential, connected.Client.Endpoint, null, null, null, checkToken);
                    if (context == null)
                    {
                        throw new InvalidOperationException();
                    }

                    var response = await context.Account.GetAsync(checkToken);
                    response.GetRawResponse().Headers.TryGetValue("Date", out var date);
                    return new ProbeResponse(null, date);
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
        checks.Add(CreateClockCheck(checks));
        checks.Add(await this.CheckUpdatesAsync(currentVersion, deadline.Token, token));
        token.ThrowIfCancellationRequested();
        return this.CreateResult(checks, commandState, shell.Options?.Quiet == true, stopwatch.ElapsedMilliseconds);
    }

    internal static async Task<DoctorCheck> RunResponseCheckAsync(
        string id,
        Func<CancellationToken, Task<ProbeResponse>> probe,
        string successCode,
        bool required,
        TimeSpan timeout,
        CancellationToken deadline,
        CancellationToken token)
    {
        ClockSample? clock = null;
        var result = await RunCheckAsync(
            id,
            async checkToken =>
            {
                var started = DateTimeOffset.UtcNow;
                var response = await probe(checkToken);
                clock = ReadClockSample(response.DateHeader, started, DateTimeOffset.UtcNow);
                return response.RequestCharge;
            },
            successCode,
            required,
            timeout,
            deadline,
            token);
        return result with { Clock = result.Status == "PASS" ? clock : null };
    }

    internal static ClockSample? ReadClockSample(string? date, DateTimeOffset started, DateTimeOffset completed)
    {
        if (completed < started || !DateTimeOffset.TryParseExact(date, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var serverTime))
        {
            return null;
        }

        var halfDuration = (completed - started).TotalSeconds / 2;
        return new ClockSample((serverTime - started).TotalSeconds - halfDuration, halfDuration + 1);
    }

    internal static DoctorCheck CreateClockCheck(IEnumerable<DoctorCheck> checks)
    {
        var sample = checks.Where(check => check.Status == "PASS" && check.Clock != null)
            .Select(check => check.Clock!).OrderBy(clock => clock.UncertaintySeconds).FirstOrDefault();
        if (sample == null)
        {
            return Check("clock", "SKIP", "clock-unavailable");
        }

        var skewed = Math.Abs(sample.OffsetSeconds) > 300 + sample.UncertaintySeconds;
        return Check("clock", skewed ? "WARN" : "PASS", skewed ? "clock-skew" : "clock-within-tolerance") with { Clock = sample };
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
                Message = Message(id == "arm" && code == "forbidden" ? "arm-forbidden" : code),
                DurationMs = stopwatch.ElapsedMilliseconds,
                RequestCharge = exception is CosmosException cosmos ? cosmos.RequestCharge : null,
            };
        }
    }

    internal static string ClassifyFailure(Exception exception)
    {
        var fallback = "probe-failed";
        Exception? current = exception;
        for (var depth = 0; current != null && depth < 16; depth++)
        {
            var status = current switch
            {
                CosmosException cosmos => (int)cosmos.StatusCode,
                global::Azure.RequestFailedException azure => azure.Status,
                HttpRequestException http => (int?)http.StatusCode ?? 0,
                _ => 0,
            };
            var code = status switch
            {
                401 => "unauthorized",
                403 => "forbidden",
                404 => "not-found",
                407 => "proxy-authentication-required",
                408 => "timeout",
                429 => "throttled",
                502 or 503 or 504 => "unreachable",
                _ => null,
            };
            if (code != null)
            {
                return code;
            }

            code = current switch
            {
                OperationCanceledException or TimeoutException => "timeout",
                AuthenticationException => "tls-failed",
                global::Azure.Identity.CredentialUnavailableException => "credential-unavailable",
                global::Azure.Identity.AuthenticationFailedException => "authentication-failed",
                SocketException socket => socket.SocketErrorCode switch
                {
                    SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData or SocketError.NoRecovery => "dns-failed",
                    SocketError.TimedOut => "timeout",
                    SocketError.ConnectionRefused => "connection-refused",
                    SocketError.ConnectionReset or SocketError.ConnectionAborted => "connection-reset",
                    _ => "unreachable",
                },
                HttpRequestException http => http.HttpRequestError switch
                {
                    HttpRequestError.NameResolutionError => "dns-failed",
                    HttpRequestError.SecureConnectionError => "tls-failed",
                    _ => null,
                },
                _ => null,
            };
            if (code != null)
            {
                return code;
            }

            if (current is HttpRequestException || status >= 500)
            {
                fallback = "unreachable";
            }

            if (current is AggregateException aggregate)
            {
                if (aggregate.InnerExceptions.Count != 1)
                {
                    return fallback;
                }

                current = aggregate.InnerExceptions[0];
            }
            else
            {
                current = current.InnerException;
            }
        }

        return fallback;
    }

    internal static bool MayPrompt(string? credentialType) => credentialType is not
        (null or "AzureCliCredential" or "ManagedIdentityCredential" or "StaticTokenCredential" or "EnvironmentCredential" or "WorkloadIdentityCredential" or "ClientSecretCredential" or "ClientCertificateCredential");

    internal async Task<DoctorCheck> CheckUpdatesAsync(string currentVersion, CancellationToken deadline, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (this.NoUpdateCheck)
        {
            return Check("updates", "SKIP", "update-check-disabled");
        }

        ShellUpdateChecker.UpdateInfo? update = null;
        var result = await RunCheckAsync(
            "updates",
            async checkToken =>
            {
                update = ShellUpdateChecker.CompareReleases(currentVersion, await this.FetchReleasesAsync(checkToken));
                return null;
            },
            "update-current",
            false,
            TimeSpan.FromSeconds(5),
            deadline,
            token);
        token.ThrowIfCancellationRequested();
        if (result.Status != "PASS")
        {
            return Check("updates", "SKIP", result.Code == "timeout" ? "update-check-timeout" : "update-check-unavailable") with { DurationMs = result.DurationMs };
        }

        var status = update!.Code == "update-available" ? "WARN" : update.Code == "update-current" ? "PASS" : "SKIP";
        return Check("updates", status, update.Code) with
        {
            DurationMs = result.DurationMs,
            LatestVersion = update.LatestVersion,
            Message = update.LatestVersion == null ? Message(update.Code) : $"{Message(update.Code)} {update.LatestVersion}",
        };
    }

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

    internal CommandState CreateResult(List<DoctorCheck> checks, CommandState commandState, bool quiet = false, long durationMs = 0)
    {
        var result = new DoctorCommandState(checks.Any(check => check.Status == "FAIL"));
        if (commandState.OutputFormatExplicitlySet)
        {
            result.OutputFormat = commandState.OutputFormat;
        }

        result.SetFormat(this.Format);
        result.RequestCharge = checks.Any(check => check.RequestCharge.HasValue) ? checks.Sum(check => check.RequestCharge ?? 0) : null;
        var summary = new
        {
            pass = checks.Count(check => check.Status == "PASS"),
            warn = checks.Count(check => check.Status == "WARN"),
            fail = checks.Count(check => check.Status == "FAIL"),
            skip = checks.Count(check => check.Status == "SKIP"),
            durationMs,
            requestCharge = result.RequestCharge,
        };
        result.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            status = result.IsError ? "FAIL" : checks.Any(check => check.Status == "WARN") ? "WARN" : "PASS",
            summary,
            checks = checks.Select(check => new { id = check.Id, status = check.Status, code = check.Code, message = check.Message, durationMs = check.DurationMs, requestCharge = check.RequestCharge, credentialType = check.CredentialType, latestVersion = check.LatestVersion, clockOffsetSeconds = check.Id == "clock" ? check.Clock?.OffsetSeconds : null, clockUncertaintySeconds = check.Id == "clock" ? check.Clock?.UncertaintySeconds : null }),
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

            var grid = new Grid();
            grid.AddColumn(new GridColumn().Width(4).NoWrap().PadRight(2));
            grid.AddColumn(new GridColumn().Width(18).NoWrap().PadRight(1));
            var separateMetrics = AnsiConsole.Profile.Width >= 100;
            if (separateMetrics)
            {
                grid.AddColumn(new GridColumn().RightAligned().NoWrap().PadRight(2));
                grid.AddColumn(new GridColumn().RightAligned().NoWrap().PadRight(2));
            }

            grid.AddColumn(new GridColumn());
            foreach (var check in checks)
            {
                var duration = $"{check.DurationMs.ToString(CultureInfo.InvariantCulture)} ms";
                var charge = $"{FormatCharge(check.RequestCharge)} RU";
                if (separateMetrics)
                {
                    grid.AddRow(FormatStatus(check.Status), Theme.FormatHelpName(check.Id), Theme.FormatMuted(duration), Theme.FormatMuted(charge), FormatCheckMessage(check));
                }
                else
                {
                    grid.AddRow(FormatStatus(check.Status), Theme.FormatHelpName(check.Id), $"{FormatCheckMessage(check)} {Theme.FormatMuted($"({duration}, {charge})")}");
                }
            }

            AnsiConsole.Write(grid);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"{Theme.FormatSectionHeader(Message("summary"))}: {summary.pass} {FormatStatus("PASS")} | {summary.warn} {FormatStatus("WARN")} | {summary.fail} {FormatStatus("FAIL")} | {summary.skip} {FormatStatus("SKIP")} | {durationMs.ToString(CultureInfo.InvariantCulture)} ms | {FormatCharge(result.RequestCharge)} RU");
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

    private static string FormatCharge(double? charge) => charge?.ToString("0.########", CultureInfo.InvariantCulture) ?? "-";

    internal sealed record DoctorCheck(string Id, string Status, string Message, string Code = "available", long DurationMs = 0, double? RequestCharge = null)
    {
        public string? CredentialType { get; init; }

        public string? LatestVersion { get; init; }

        public ClockSample? Clock { get; init; }
    }

    internal sealed record ProbeResponse(double? RequestCharge, string? DateHeader);

    internal sealed record ClockSample(double OffsetSeconds, double UncertaintySeconds);

    private sealed class DoctorCommandState(bool failed) : CommandState
    {
        public override bool IsError => failed;

        public override int ExitCode => failed ? 1 : 0;
    }
}