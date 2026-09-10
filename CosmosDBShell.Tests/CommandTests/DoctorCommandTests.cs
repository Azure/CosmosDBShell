namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using Spectre.Console;
using global::Azure.Core;
using global::Azure.ResourceManager;
using global::Azure.ResourceManager.CosmosDB;

[Collection(ConsoleOutputTestCollection.Name)]
public class DoctorCommandTests
{
    [Theory]
    [InlineData("doctor who --no-update-check --format json")]
    [InlineData("doctor WHO --no-update-check --format json")]
    [InlineData("doctor Who --no-update-check --format json")]
    [InlineData("doctor \" who \" --no-update-check --format json")]
    [InlineData("doctor --who --no-update-check --format json")]
    public async Task WhoAliases_ReturnStructuredContextWithoutTextHeading(string command)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();

        var result = await shell.RunCommandAsync(new CommandState(), command, CancellationToken.None);

        Assert.False(result.IsError);
        var output = result.GenerateOutputText();
        using var report = JsonDocument.Parse(output);
        var checks = report.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Contains(checks, check => check.GetProperty("id").GetString() == "identity" && check.GetProperty("status").GetString() == "SKIP");
        Assert.Contains(checks, check => check.GetProperty("id").GetString() == "write-access" && check.GetProperty("status").GetString() == "SKIP");
        Assert.DoesNotContain("Doctor Who?", output);
        var mcp = McpResponseFactory.CreateSuccess(result, shell.State);
        Assert.Contains("write-not-assessed", mcp.StructuredContent!.Value.ToString());
    }

    [Fact]
    public async Task Disconnected_ReturnsVersionedReportWithoutChangingState()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var originalState = new DisconnectedState();
        shell.State = originalState;

        var result = await new DoctorCommand { NoUpdateCheck = true, Format = "json" }.ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None);

        using var report = JsonDocument.Parse(result.GenerateOutputText());
        Assert.Equal(1, report.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("PASS", report.RootElement.GetProperty("status").GetString());
        Assert.Contains(report.RootElement.GetProperty("checks").EnumerateArray(), check => check.GetProperty("id").GetString() == "connection" && check.GetProperty("status").GetString() == "SKIP");
        Assert.Equal(0, result.ExitCode);
        Assert.Same(originalState, shell.State);
        Assert.DoesNotContain(report.RootElement.GetProperty("checks").EnumerateArray(), check => check.GetProperty("id").GetString() == "identity");
    }

    [Theory]
    [InlineData("AzureCliCredential")]
    [InlineData("DefaultAzureCredential")]
    [InlineData("AccountKey")]
    [InlineData("Emulator")]
    [InlineData("SECRET_CUSTOM_CREDENTIAL")]
    [InlineData(null)]
    public async Task Who_ReportsOnlyKnownCredentialMetadataWithoutAcquiringIdentity(string? credentialType)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var credential = Substitute.For<TokenCredential>();
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://private-account.documents.azure.com"));
        client.ClientOptions.Returns(new CosmosClientOptions());
        shell.Connect(client, credential: credential, credentialTypeOverride: credentialType);
        shell.State = new ContainerState("private-container", "private-database", client);
        var state = shell.State;

        var result = await new DoctorCommand { NoUpdateCheck = true, Who = true, Format = "json", ResolveHostAsync = (_, _) => Task.CompletedTask }
            .ExecuteAsync(shell, new CommandState(), "doctor --who", CancellationToken.None);

        var output = result.GenerateOutputText();
        using var report = JsonDocument.Parse(output);
        var checks = report.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        var identity = Assert.Single(checks, check => check.GetProperty("id").GetString() == "identity");
        var known = credentialType != null && credentialType != "SECRET_CUSTOM_CREDENTIAL";
        Assert.Equal(known ? credentialType : null, identity.GetProperty("credentialType").GetString());
        Assert.Equal(known ? "PASS" : "SKIP", identity.GetProperty("status").GetString());
        Assert.Contains(checks, check => check.GetProperty("code").GetString() == "scope-container");
        Assert.DoesNotContain("SECRET_CUSTOM_CREDENTIAL", output);
        Assert.DoesNotContain("private-", output);
        Assert.Empty(credential.ReceivedCalls());
        await client.DidNotReceive().ReadAccountAsync();
        Assert.Same(state, shell.State);
    }

    [Fact]
    public async Task Who_TextHeadingIsOptIn()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var normal = await new DoctorCommand { NoUpdateCheck = true }.ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None);
        var who = await new DoctorCommand { NoUpdateCheck = true, Who = true }.ExecuteAsync(shell, new CommandState(), "doctor --who", CancellationToken.None);

        Assert.DoesNotContain("Doctor Who?", CaptureConsole(() => normal.RenderUser!()));
        Assert.StartsWith("Doctor Who?", CaptureConsole(() => who.RenderUser!()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownSubcommand_IsUsageError()
    {
        using var shell = ShellInterpreter.CreateInstance();
        await Assert.ThrowsAsync<CommandException>(() => new DoctorCommand { Subcommand = "unknown" }
            .ExecuteAsync(shell, new CommandState(), "doctor unknown", CancellationToken.None));
    }

    [Theory]
    [InlineData("doctor --database db --no-update-check --format json")]
    [InlineData("doctor --arm --no-update-check --format json")]
    public async Task RegisteredCommand_ExplicitDisconnectedChecksFailWithReport(string command)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var result = await shell.RunCommandAsync(new CommandState(), command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(1, result.ExitCode);
        using var report = JsonDocument.Parse(result.GenerateOutputText());
        Assert.Equal("FAIL", report.RootElement.GetProperty("status").GetString());
        var mcp = McpResponseFactory.CreateSuccess(result, shell.State);
        Assert.True(mcp.IsError);
        Assert.Equal(1, mcp.StructuredContent!.Value.GetProperty("result").GetProperty("schemaVersion").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public async Task InvalidTimeout_IsUsageError(int timeout)
    {
        using var shell = ShellInterpreter.CreateInstance();
        await Assert.ThrowsAsync<CommandException>(() => new DoctorCommand { Timeout = timeout }.ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None));
    }

    [Fact]
    public async Task QueryWithoutContainer_IsUsageError()
    {
        using var shell = ShellInterpreter.CreateInstance();
        await Assert.ThrowsAsync<CommandException>(() => new DoctorCommand { Query = true }.ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None));
    }

    [Theory]
    [InlineData(401, "unauthorized")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not-found")]
    [InlineData(407, "proxy-authentication-required")]
    [InlineData(408, "timeout")]
    [InlineData(429, "throttled")]
    [InlineData(503, "unreachable")]
    public async Task ServiceFailures_AreRedactedAndClassified(int status, string code)
    {
        const string secret = "AccountKey=KNOWN_KEY;Bearer KNOWN_TOKEN;https://user:password@proxy";
        var check = await DoctorCommand.RunCheckAsync(
            "access",
            _ => throw new CosmosException(secret, (HttpStatusCode)status, 0, "private-activity", 2.5),
            "account-readable",
            true,
            TimeSpan.FromSeconds(1),
            CancellationToken.None,
            CancellationToken.None);

        Assert.Equal("FAIL", check.Status);
        Assert.Equal(code, check.Code);
        var result = new DoctorCommand { Format = "json" }.CreateResult([check], new CommandState());
        var output = result.GenerateOutputText();
        Assert.DoesNotContain("KNOWN_KEY", output);
        Assert.DoesNotContain("KNOWN_TOKEN", output);
        Assert.DoesNotContain("password", output);
        Assert.DoesNotContain("private-activity", output);
        Assert.Equal(2.5, result.RequestCharge);
        var text = CaptureConsole(() => result.RenderUser!());

        Assert.Contains("FAIL", text);
        Assert.DoesNotContain("KNOWN_KEY", text);
        Assert.DoesNotContain("KNOWN_TOKEN", text);
        Assert.DoesNotContain("password", text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderUser_QuietSuppressesTextButPreservesReport(bool who)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Quiet = true };
        shell.State = new DisconnectedState();
        var result = await new DoctorCommand { NoUpdateCheck = true, Who = who, Database = "missing", Format = "json" }
            .ExecuteAsync(shell, new CommandState(), "doctor --database missing", CancellationToken.None);

        Assert.Empty(CaptureConsole(() => result.RenderUser!(), color: true));
        Assert.Equal(1, result.ExitCode);
        using var report = JsonDocument.Parse(result.GenerateOutputText());
        Assert.Equal("FAIL", report.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void RenderUser_ColorsEachStatusDistinctly()
    {
        var checks = new List<DoctorCommand.DoctorCheck>
        {
            new("shell", "PASS", "pass message"),
            new("authentication", "WARN", "warn message"),
            new("access", "FAIL", "fail message"),
            new("query", "SKIP", "skip message"),
        };
        var result = new DoctorCommand().CreateResult(checks, new CommandState());

        var colored = CaptureConsole(() => result.RenderUser!(), color: true);
        var plain = CaptureConsole(() => result.RenderUser!());

        var lines = colored.Split('\n');
        var prefixes = lines.Take(4).Select(line => line[..line.IndexOf("  ", StringComparison.Ordinal)]).ToArray();
        Assert.Equal(4, prefixes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(prefixes, prefix => Assert.Contains('\u001b', prefix));
        Assert.Contains("PASS  shell", plain, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', plain);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    public void RenderUser_WrapsMessagesWithinTheirColumn(int width)
    {
        var result = new DoctorCommand().CreateResult(
            [new("access", "FAIL", "[literal] " + string.Join(" ", Enumerable.Repeat("wrapped", 30)))], new CommandState());

        var output = CaptureConsole(() => result.RenderUser!(), width: width);
        var lines = output.Split('\n').Select(line => line.TrimEnd('\r')).TakeWhile(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        Assert.True(lines.Length > 1);
        Assert.Contains("[literal]", lines[0]);
        Assert.All(lines, line => Assert.True(line.Length <= width));
        var messageColumn = lines[0].IndexOf("[literal]", StringComparison.Ordinal);
        Assert.All(lines.Skip(1), line => Assert.StartsWith(new string(' ', messageColumn), line));
    }

    [Theory]
    [InlineData("arm", true)]
    [InlineData("access", false)]
    [InlineData("query", false)]
    public async Task Forbidden_UsesScopeSpecificGuidance(string id, bool managementPlane)
    {
        var check = await DoctorCommand.RunCheckAsync(id,
            _ => throw new global::Azure.RequestFailedException(403, "SECRET_TOKEN"),
            "unused", true, TimeSpan.FromSeconds(1), CancellationToken.None, CancellationToken.None);

        Assert.Equal("forbidden", check.Code);
        Assert.Equal("FAIL", check.Status);
        Assert.Equal(managementPlane, check.Message.Contains("management-plane RBAC", StringComparison.Ordinal));
        Assert.DoesNotContain("SECRET_TOKEN", check.Message);
        if (!managementPlane)
        {
            Assert.Contains("data-plane permissions", check.Message);
        }
    }

    public static TheoryData<Exception, string> TransportFailures => new()
    {
        { new HttpRequestException("secret", new System.Security.Authentication.AuthenticationException("secret")), "tls-failed" },
        { new HttpRequestException(HttpRequestError.SecureConnectionError), "tls-failed" },
        { new HttpRequestException(HttpRequestError.NameResolutionError), "dns-failed" },
        { new HttpRequestException("secret", new SocketException((int)SocketError.HostNotFound)), "dns-failed" },
        { new SocketException((int)SocketError.TryAgain), "dns-failed" },
        { new SocketException((int)SocketError.NoData), "dns-failed" },
        { new SocketException((int)SocketError.NoRecovery), "dns-failed" },
        { new SocketException((int)SocketError.ConnectionRefused), "connection-refused" },
        { new HttpRequestException("secret", new SocketException((int)SocketError.ConnectionReset)), "connection-reset" },
        { new SocketException((int)SocketError.ConnectionAborted), "connection-reset" },
        { new SocketException((int)SocketError.NetworkUnreachable), "unreachable" },
        { new SocketException((int)SocketError.TimedOut), "timeout" },
        { new HttpRequestException("secret", new TimeoutException()), "timeout" },
        { new HttpRequestException("secret", null, HttpStatusCode.Forbidden), "forbidden" },
        { new HttpRequestException("secret", null, HttpStatusCode.ProxyAuthenticationRequired), "proxy-authentication-required" },
        { new HttpRequestException("secret", null, HttpStatusCode.RequestTimeout), "timeout" },
        { new HttpRequestException("secret", null, HttpStatusCode.ServiceUnavailable), "unreachable" },
        { new global::Azure.RequestFailedException(0, "secret", new HttpRequestException(HttpRequestError.SecureConnectionError)), "tls-failed" },
        { new global::Azure.RequestFailedException(403, "secret", new SocketException((int)SocketError.HostNotFound)), "forbidden" },
        { new global::Azure.Identity.CredentialUnavailableException("secret"), "credential-unavailable" },
        { new global::Azure.Identity.AuthenticationFailedException("secret"), "authentication-failed" },
        { new AggregateException(new HttpRequestException(HttpRequestError.NameResolutionError)), "dns-failed" },
        { new AggregateException(new TimeoutException(), new SocketException((int)SocketError.HostNotFound)), "probe-failed" },
        { new InvalidOperationException("secret"), "probe-failed" },
        { new HttpRequestException("secret"), "unreachable" },
    };

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TransportFailures_AreClassifiedWithoutExposingExceptions(Exception exception, string expected)
    {
        var check = await DoctorCommand.RunCheckAsync("access", _ => Task.FromException<double?>(exception),
            "unused", true, TimeSpan.FromSeconds(1), CancellationToken.None, CancellationToken.None);

        Assert.Equal(expected, check.Code);
        Assert.Equal("FAIL", check.Status);
        var result = new DoctorCommand { Format = "json" }.CreateResult([check], new CommandState());
        Assert.DoesNotContain("secret", result.GenerateOutputText());
        Assert.DoesNotContain("secret", CaptureConsole(() => result.RenderUser!()));
        Assert.DoesNotContain("command-doctor-", check.Message);
    }

    [Fact]
    public void DeepExceptionChain_IsBounded()
    {
        Exception exception = new System.Security.Authentication.AuthenticationException();
        for (var depth = 0; depth < 32; depth++)
        {
            exception = new InvalidOperationException("secret", exception);
        }

        Assert.Equal("probe-failed", DoctorCommand.ClassifyFailure(exception));
    }

    [Fact]
    public async Task Timeout_BoundsEvenANonCooperativeProbe()
    {
        var pending = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = await DoctorCommand.RunCheckAsync("access", _ => pending.Task, "account-readable", true, TimeSpan.FromMilliseconds(20), CancellationToken.None, CancellationToken.None);
        pending.SetResult(null);
        Assert.Equal("timeout", check.Code);
        Assert.Equal("FAIL", check.Status);
    }

    [Fact]
    public async Task ExpiredDeadline_DoesNotStartAnotherProbe()
    {
        using var deadline = new CancellationTokenSource();
        await deadline.CancelAsync();
        var invoked = false;
        var check = await DoctorCommand.RunCheckAsync("arm", _ => { invoked = true; return Task.FromResult<double?>(null); }, "arm-readable", false, TimeSpan.FromSeconds(1), deadline.Token, CancellationToken.None);
        Assert.False(invoked);
        Assert.Equal("WARN", check.Status);
        Assert.Equal("timeout", check.Code);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DoctorCommand.RunCheckAsync("access", _ => Task.FromResult<double?>(null), "account-readable", true, TimeSpan.FromSeconds(1), CancellationToken.None, cancellation.Token));
    }

    [Fact]
    public void ExplicitDatabase_DoesNotInheritCurrentContainer()
    {
        using var client = new CosmosClient("https://localhost:8081", Convert.ToBase64String(new byte[64]));
        var state = new ContainerState("old-container", "old-database", client);
        Assert.Equal(("new-database", (string?)null), new DoctorCommand { Database = "new-database" }.ResolveTarget(state));
        Assert.Equal(("old-database", "new-container"), new DoctorCommand { Container = "new-container" }.ResolveTarget(state));
    }

    [Theory]
    [InlineData("DefaultAzureCredential", true)]
    [InlineData("InteractiveBrowserCredential", true)]
    [InlineData("DeviceCodeCredential", true)]
    [InlineData("VisualStudioCodeCredential", true)]
    [InlineData("ManagedIdentityCredential", false)]
    [InlineData("AzureCliCredential", false)]
    [InlineData(null, false)]
    public void InteractiveCredentials_AreNotProbed(string? credentialType, bool expected)
    {
        Assert.Equal(expected, DoctorCommand.MayPrompt(credentialType));
    }

    [Fact]
    public async Task DnsFailure_SkipsDependentChecksWithoutLeakingConnection()
    {
        var key = Convert.ToBase64String(new byte[64]);
        using var shell = ShellInterpreter.CreateInstance();
        shell.Connect(new CosmosClient("https://private-account.documents.azure.com", key));
        var state = shell.State;
        var result = await new DoctorCommand
        {
            NoUpdateCheck = true,
            Format = "json",
            ResolveHostAsync = (_, _) => throw new SocketException((int)SocketError.HostNotFound),
        }.ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None);

        var output = result.GenerateOutputText();
        Assert.DoesNotContain(key, output);
        Assert.DoesNotContain("private-account", output);
        Assert.Contains("dns-failed", output);
        Assert.Contains("dependency-failed", output);
        Assert.True(result.IsError);
        Assert.Same(state, shell.State);
    }

    [Theory]
    [InlineData("query", "Sat, 01 Jan 2000 00:00:00 GMT", "WARN")]
    [InlineData("container", "Sat, 01 Jan 2000 00:00:00 GMT", "WARN")]
    [InlineData("query", "SECRET_DATE", "SKIP")]
    [InlineData("query", null, "SKIP")]
    public async Task Query_UsesOneConstantProjectionPageAndPreservesState(string dateSource, string? dateHeader, string clockStatus)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://private-account.documents.azure.com"));
        client.ClientOptions.Returns(new CosmosClientOptions { ConnectionMode = ConnectionMode.Direct });
        var container = Substitute.For<Container>();
        client.GetContainer("database", "container").Returns(container);
        var metadata = Substitute.For<ContainerResponse>();
        metadata.RequestCharge.Returns(1.5);
        var headers = new Headers();
        if (dateSource == "container" && dateHeader != null)
        {
            headers.Add("Date", dateHeader);
        }

        metadata.Headers.Returns(headers);
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(metadata);
        var iterator = Substitute.For<FeedIterator>();
        container.GetItemQueryStreamIterator("SELECT TOP 1 VALUE 1 FROM c", null, Arg.Is<QueryRequestOptions>(options => options.MaxItemCount == 1 && options.MaxConcurrency == 1)).Returns(iterator);
        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("SECRET_DOCUMENT"));
        using var response = new ResponseMessage(HttpStatusCode.OK) { Content = content };
        response.Headers.Add("x-ms-request-charge", "2");
        if (dateSource == "query" && dateHeader != null)
        {
            response.Headers.Add("Date", dateHeader);
        }
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(response);
        shell.State = new ContainerState("container", "database", client);
        var state = shell.State;

        var result = await new DoctorCommand { NoUpdateCheck = true, Who = true, Query = true, Format = "json", ResolveHostAsync = (_, _) => Task.CompletedTask }.ExecuteAsync(shell, new CommandState(), "doctor --who", CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(3.5, result.RequestCharge);
        Assert.Contains("query-readable", result.GenerateOutputText());
        Assert.Contains("write-not-assessed", result.GenerateOutputText());
        Assert.DoesNotContain("SECRET_DOCUMENT", result.GenerateOutputText());
        Assert.DoesNotContain("SECRET_DATE", result.GenerateOutputText());
        using var report = JsonDocument.Parse(result.GenerateOutputText());
        var clock = Assert.Single(report.RootElement.GetProperty("checks").EnumerateArray(), check => check.GetProperty("id").GetString() == "clock");
        Assert.Equal(clockStatus, clock.GetProperty("status").GetString());
        await container.Received(1).ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>());
        Assert.False(content.CanRead);
        await iterator.Received(1).ReadNextAsync(Arg.Any<CancellationToken>());
        iterator.Received(1).Dispose();
        Assert.Same(state, shell.State);
    }

    [Fact]
    public void McpAnnotation_IsReadOnlyAndUnrestricted()
    {
        var attribute = Assert.Single(typeof(DoctorCommand).GetCustomAttributes(typeof(McpAnnotationAttribute), false).Cast<McpAnnotationAttribute>());
        Assert.True(attribute.ReadOnly);
        Assert.False(attribute.Destructive);
        Assert.False(attribute.Restricted);
    }

    [Theory]
    [InlineData(-600, "WARN")]
    [InlineData(600, "WARN")]
    [InlineData(300, "PASS")]
    [InlineData(-300, "PASS")]
    [InlineData(0, "PASS")]
    public void ClockCheck_AccountsForRequestTimeAndTimestampResolution(int offset, string status)
    {
        var started = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = DoctorCommand.ReadClockSample(started.AddSeconds(offset).ToString("r"), started, started.AddSeconds(4));
        Assert.NotNull(clock);
        Assert.Equal(offset - 2, clock.OffsetSeconds);
        Assert.Equal(3, clock.UncertaintySeconds);
        var check = DoctorCommand.CreateClockCheck([new("access", "PASS", "read") { Clock = clock }]);
        Assert.Equal(status, check.Status);
        var result = new DoctorCommand().CreateResult([check], new CommandState());
        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.GenerateOutputText());
        Assert.Equal(offset - 2, report.RootElement.GetProperty("checks")[0].GetProperty("clockOffsetSeconds").GetDouble());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SECRET_DATE")]
    public void ClockCheck_UnusableDateIsSkipped(string? date)
    {
        var now = DateTimeOffset.UtcNow;
        var clock = DoctorCommand.ReadClockSample(date, now, now);
        Assert.Null(clock);
        var check = DoctorCommand.CreateClockCheck([new("access", "PASS", "read") { Clock = clock }]);
        Assert.Equal("SKIP", check.Status);
        Assert.Equal("clock-unavailable", check.Code);
        Assert.DoesNotContain("SECRET_DATE", check.Message);
    }

    [Fact]
    public void ClockCheck_UsesLowestUncertaintyAndIgnoresFailedProbes()
    {
        var check = DoctorCommand.CreateClockCheck([
            new("access", "PASS", "read") { Clock = new(500, 200) },
            new("query", "PASS", "read") { Clock = new(600, 1) },
            new("arm", "FAIL", "failed") { Clock = new(0, 0) },
        ]);
        Assert.Equal("WARN", check.Status);
        Assert.Equal(600, check.Clock!.OffsetSeconds);
        var now = DateTimeOffset.UtcNow;
        Assert.Null(DoctorCommand.ReadClockSample(now.ToString("r"), now, now.AddSeconds(-1)));
    }

    [Fact]
    public async Task DatabaseResponse_ProvidesClockSampleWithoutExtraReads()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://private-account.documents.azure.com"));
        client.ClientOptions.Returns(new CosmosClientOptions());
        var database = Substitute.For<Database>();
        client.GetDatabase("database").Returns(database);
        var response = Substitute.For<DatabaseResponse>();
        var headers = new Headers();
        headers.Add("Date", "Sat, 01 Jan 2000 00:00:00 GMT");
        response.Headers.Returns(headers);
        response.RequestCharge.Returns(1.25);
        database.ReadAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>()).Returns(response);
        shell.State = new DatabaseState("database", client);

        var result = await new DoctorCommand { NoUpdateCheck = true, ResolveHostAsync = (_, _) => Task.CompletedTask }
            .ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None);

        Assert.Contains("clock-skew", result.GenerateOutputText());
        Assert.Equal(1.25, result.RequestCharge);
        Assert.Equal(0, result.ExitCode);
        await database.Received(1).ReadAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>());
        await client.DidNotReceive().ReadAccountAsync();
    }

    [Fact]
    public void Summary_PreservesUnknownCharges()
    {
        var result = new DoctorCommand().CreateResult([new("clock", "SKIP", "no date")], new CommandState());
        using var report = JsonDocument.Parse(result.GenerateOutputText());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("summary").GetProperty("requestCharge").ValueKind);
        Assert.Null(result.RequestCharge);
        Assert.Contains("- RU", CaptureConsole(() => result.RenderUser!()));
    }

    [Theory]
    [InlineData(false, false, "PASS", 0)]
    [InlineData(false, true, "WARN", 0)]
    [InlineData(true, true, "FAIL", 1)]
    public async Task ExistingArmContext_IsReadWithoutChangingState(bool required, bool fails, string status, int exitCode)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://private-account.documents.azure.com"));
        client.ClientOptions.Returns(new CosmosClientOptions());
        client.ReadAccountAsync().Returns(Substitute.For<AccountProperties>());
        var account = Substitute.For<CosmosDBAccountResource>();
        var rawResponse = Substitute.ForPartsOf<global::Azure.Response>();
        Assert.False(rawResponse.Headers.TryGetValue("Date", out _));
        account.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => fails
            ? Task.FromException<global::Azure.Response<CosmosDBAccountResource>>(new global::Azure.RequestFailedException(403, "SECRET_TOKEN"))
            : Task.FromResult(global::Azure.Response.FromValue(account, rawResponse)));
        var context = new ArmCosmosContext(
            Substitute.For<ArmClient>(),
            new ResourceIdentifier("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/group/providers/Microsoft.DocumentDB/databaseAccounts/account"),
            "subscription",
            "group",
            "account",
            client.Endpoint,
            account);
        shell.State = new ConnectedState(client, context);
        var state = shell.State;

        var result = await new DoctorCommand { NoUpdateCheck = true, Arm = required, Format = "json", ResolveHostAsync = (_, _) => Task.CompletedTask }.ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None);

        using var report = JsonDocument.Parse(result.GenerateOutputText());
        Assert.Equal(status, report.RootElement.GetProperty("status").GetString());
        Assert.Equal(exitCode, result.ExitCode);
        Assert.DoesNotContain("SECRET_TOKEN", result.GenerateOutputText());
        await account.Received(1).GetAsync(Arg.Any<CancellationToken>());
        Assert.Same(state, shell.State);
        Assert.Same(context, ((ConnectedState)shell.State).ArmContext);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AzureCliCredential")]
    [InlineData("ManagedIdentityCredential")]
    public async Task InteractiveCredential_IsNeverAcquiredByDoctor(string? credentialTypeOverride)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var credential = Substitute.For<TokenCredential>();
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://private-account.documents.azure.com"));
        client.ClientOptions.Returns(new CosmosClientOptions());
        shell.Connect(client, credential: credential, credentialTypeOverride: credentialTypeOverride);

        var result = await new DoctorCommand { NoUpdateCheck = true, Arm = true, Query = true, Database = "database", Container = "container", Format = "json", ResolveHostAsync = (_, _) => Task.CompletedTask }.ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("interactive-credential", result.GenerateOutputText());
        using var report = JsonDocument.Parse(result.GenerateOutputText());
        foreach (var id in new[] { "access", "query" })
        {
            var check = Assert.Single(report.RootElement.GetProperty("checks").EnumerateArray(), check => check.GetProperty("id").GetString() == id);
            Assert.Equal("SKIP", check.GetProperty("status").GetString());
            Assert.Equal("interactive-credential", check.GetProperty("code").GetString());
        }

        Assert.Empty(credential.ReceivedCalls());
        await client.DidNotReceive().ReadAccountAsync();
    }

    private static string CaptureConsole(Action action, bool color = false, int width = 120)
    {
        var saved = AnsiConsole.Console;
        using var writer = new StringWriter();
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = color ? AnsiSupport.Yes : AnsiSupport.No,
                ColorSystem = color ? ColorSystemSupport.Standard : ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer),
            });
            AnsiConsole.Console.Profile.Width = width;

            action();
        }
        finally
        {
            AnsiConsole.Console = saved;
        }

        return writer.ToString();
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    public void RenderUser_ShowsMeasuredCostsDurationsAndSummary(int width)
    {
        var result = new DoctorCommand().CreateResult([
            new("access", "PASS", "read", DurationMs: 123, RequestCharge: 1.25),
            new("arm", "WARN", "unavailable", DurationMs: 456),
            new("query", "FAIL", "failed", DurationMs: 789, RequestCharge: 0),
            new("clock", "SKIP", "no date"),
        ], new CommandState(), durationMs: 1500);
        var text = CaptureConsole(() => result.RenderUser!(), width: width);
        Assert.Contains("123 ms", text);
        Assert.Contains("456 ms", text);
        Assert.Contains("789 ms", text);
        Assert.Contains("1.25 RU", text);
        Assert.Contains("0 RU", text);
        Assert.Contains("- RU", text);
        Assert.Contains("Summary:", text);
        Assert.Contains("1 PASS", text);
        Assert.Contains("1 WARN", text);
        Assert.Contains("1 FAIL", text);
        Assert.Contains("1 SKIP", text);
        Assert.Contains("1500 ms", text);
        using var report = JsonDocument.Parse(result.GenerateOutputText());
        var summary = report.RootElement.GetProperty("summary");
        foreach (var status in new[] { "pass", "warn", "fail", "skip" })
        {
            Assert.Equal(1, summary.GetProperty(status).GetInt32());
        }

        Assert.Equal(1500, summary.GetProperty("durationMs").GetInt64());
        Assert.Equal(1.25, summary.GetProperty("requestCharge").GetDouble());
    }
}