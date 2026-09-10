namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;

public class ShellUpdateCheckerTests
{
    [Fact]
    public async Task FetchReleases_UsesOnlyPublicMetadataEndpoint()
    {
        using var handler = new ReleaseHandler();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        handler.Response = response;
        using var client = new HttpClient(handler);
        var releases = await ShellUpdateChecker.FetchReleasesAsync(client, CancellationToken.None);
        Assert.Equal(JsonValueKind.Array, releases.ValueKind);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Doctor_ChecksUpdatesWhileDisconnectedWithoutInstalling()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var originalState = shell.State;
        var calls = 0;
        var command = new DoctorCommand
        {
            FetchReleasesAsync = _ =>
            {
                calls++;
                return Task.FromResult(JsonSerializer.SerializeToElement(new[] { new { tag_name = "v999.0.0", prerelease = false, draft = false } }));
            },
        };
        var result = await command.ExecuteAsync(shell, new CommandState(), "doctor", CancellationToken.None);
        using var report = JsonDocument.Parse(result.GenerateOutputText());
        var checks = report.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        var update = Assert.Single(checks, check => check.GetProperty("id").GetString() == "updates");
        Assert.Equal("WARN", update.GetProperty("status").GetString());
        Assert.Equal("999.0.0", update.GetProperty("latestVersion").GetString());
        Assert.Equal("update-available", update.GetProperty("code").GetString());
        Assert.DoesNotContain(checks, check => check.GetProperty("id").GetString() == "installation");
        Assert.Equal(ShellInterpreter.GetDisplayVersion(typeof(ShellInterpreter).Assembly),
            Assert.Single(checks, check => check.GetProperty("id").GetString() == "shell").GetProperty("message").GetString());
        Assert.Equal(1, calls);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.RequestCharge);
        Assert.Same(originalState, shell.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Doctor_UpdateFailuresAreSkippedWithoutRawErrors(bool malformed)
    {
        var command = new DoctorCommand
        {
            FetchReleasesAsync = _ => malformed
                ? Task.FromResult(JsonSerializer.SerializeToElement(new { secret = "SECRET_RESPONSE" }))
                : throw new HttpRequestException("SECRET_RESPONSE", null, HttpStatusCode.Forbidden),
        };
        var check = await command.CheckUpdatesAsync("1.1.0", CancellationToken.None, CancellationToken.None);
        Assert.Equal("SKIP", check.Status);
        Assert.Equal("update-check-unavailable", check.Code);
        Assert.DoesNotContain("SECRET_RESPONSE", check.Message);
        Assert.Null(check.LatestVersion);
    }

    [Fact]
    public async Task Doctor_UpdateOptOutAndExpiredBudgetDoNotFetch()
    {
        var calls = 0;
        Task<JsonElement> Fetch(CancellationToken token)
        {
            calls++;
            throw new InvalidOperationException("Must not fetch");
        }

        var disabled = await new DoctorCommand { NoUpdateCheck = true, FetchReleasesAsync = Fetch }
            .CheckUpdatesAsync("1.1.0", CancellationToken.None, CancellationToken.None);
        Assert.Equal("update-check-disabled", disabled.Code);
        var expired = await new DoctorCommand { FetchReleasesAsync = Fetch }
            .CheckUpdatesAsync("1.1.0", new CancellationToken(true), CancellationToken.None);
        Assert.Equal("update-check-timeout", expired.Code);
        Assert.Equal("SKIP", expired.Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Doctor_UpdateBudgetAndCallerCancellationBoundNoncooperativeFetch()
    {
        var pending = new TaskCompletionSource<JsonElement>();
        var command = new DoctorCommand { FetchReleasesAsync = _ => pending.Task };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var check = await command.CheckUpdatesAsync("1.1.0", deadline.Token, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal("update-check-timeout", check.Code);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.CheckUpdatesAsync("1.1.0", CancellationToken.None, caller.Token));
        pending.SetResult(JsonSerializer.SerializeToElement(Array.Empty<object>()));
    }

    [Theory]
    [InlineData("1.1.190-preview", "v1.1.209", true, "update-available", "1.1.209-preview")]
    [InlineData("1.1.209-preview+abc", "v1.1.209", true, "update-current", "1.1.209-preview")]
    [InlineData("1.1.210-preview", "v1.1.209", true, "update-current", "1.1.209-preview")]
    [InlineData("1.1.209-preview", "v1.1.209", false, "update-available", "1.1.209")]
    [InlineData("1.1.209", "v1.2.0", true, "update-no-release", null)]
    [InlineData("1.1.9", "v1.1.10", false, "update-available", "1.1.10")]
    [InlineData("1.2.0-preview.2", "v1.2.0-preview.10", true, "update-available", "1.2.0-preview.10")]
    [InlineData("unknown", "v1.1.209", false, "update-version-unknown", null)]
    [InlineData("1.1.209", "SECRET_INVALID_TAG", false, "update-no-release", null)]
    public void CompareReleases_UsesSemanticVersionsAndReleaseChannel(string current, string tag, bool prerelease, string code, string? latest)
    {
        var releases = JsonSerializer.SerializeToElement(new[] { new { tag_name = tag, prerelease, draft = false } });
        var result = ShellUpdateChecker.CompareReleases(current, releases);
        Assert.Equal(code, result.Code);
        Assert.Equal(latest, result.LatestVersion);
    }

    [Fact]
    public void CompareReleases_IgnoresDraftsAndDoesNotRelyOnResponseOrder()
    {
        var releases = JsonSerializer.SerializeToElement(new[]
        {
            new { tag_name = "v1.2.0", prerelease = false, draft = false },
            new { tag_name = "v9.0.0", prerelease = false, draft = true },
            new { tag_name = "v1.3.0", prerelease = false, draft = false },
        });
        Assert.Equal("1.3.0", ShellUpdateChecker.CompareReleases("1.1.0", releases).LatestVersion);
        Assert.Equal("update-no-release", ShellUpdateChecker.CompareReleases("1.1.0", JsonSerializer.SerializeToElement(Array.Empty<object>())).Code);
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = null!;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.github.com/repos/Azure/CosmosDBShell/releases?per_page=100", request.RequestUri!.AbsoluteUri);
            Assert.Equal("CosmosDBShell-doctor", request.Headers.UserAgent.ToString());
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Content);
            Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(this.Response);
        }
    }
}