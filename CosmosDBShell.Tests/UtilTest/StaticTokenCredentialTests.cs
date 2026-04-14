// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Util;

namespace CosmosShell.Tests.UtilTest;

public class StaticTokenCredentialTests
{
    [Fact]
    public async Task GetTokenAsync_ReturnsSuppliedToken()
    {
        var jwt = BuildTestJwt(DateTimeOffset.UtcNow.AddHours(1));
        var credential = new StaticTokenCredential(jwt);

        var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(), TestContext.Current.CancellationToken);

        Assert.Equal(jwt, token.Token);
    }

    [Fact]
    public void GetToken_ReturnsSuppliedToken()
    {
        var jwt = BuildTestJwt(DateTimeOffset.UtcNow.AddHours(1));
        var credential = new StaticTokenCredential(jwt);

        var token = credential.GetToken(new Azure.Core.TokenRequestContext(), TestContext.Current.CancellationToken);

        Assert.Equal(jwt, token.Token);
    }

    [Fact]
    public async Task GetTokenAsync_WithValidJwt_ParsesExpiry()
    {
        var expectedExpiry = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var jwt = BuildTestJwt(expectedExpiry);
        var credential = new StaticTokenCredential(jwt);

        var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(), TestContext.Current.CancellationToken);

        Assert.Equal(expectedExpiry, token.ExpiresOn);
    }

    [Fact]
    public async Task GetTokenAsync_WithNonJwtString_UsesFallbackExpiry()
    {
        var credential = new StaticTokenCredential("not-a-jwt-token");
        var before = DateTimeOffset.UtcNow;

        var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(), TestContext.Current.CancellationToken);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal("not-a-jwt-token", token.Token);
        Assert.InRange(token.ExpiresOn, before.AddMinutes(59), after.AddMinutes(61));
    }

    [Fact]
    public async Task GetTokenAsync_WithMalformedPayload_UsesFallbackExpiry()
    {
        // Three segments but middle one is not valid base64/JSON
        var jwt = "header.!!invalid!!.signature";
        var credential = new StaticTokenCredential(jwt);
        var before = DateTimeOffset.UtcNow;

        var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(), TestContext.Current.CancellationToken);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(jwt, token.Token);
        Assert.InRange(token.ExpiresOn, before.AddMinutes(59), after.AddMinutes(61));
    }

    [Fact]
    public void TryParseJwtExpiry_ValidJwt_ReturnsExpiry()
    {
        var expectedExpiry = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var jwt = BuildTestJwt(expectedExpiry);

        var result = StaticTokenCredential.TryParseJwtExpiry(jwt);

        Assert.NotNull(result);
        Assert.Equal(expectedExpiry, result.Value);
    }

    [Fact]
    public void TryParseJwtExpiry_NoExpClaim_ReturnsNull()
    {
        var payload = Base64UrlEncode("{\"sub\":\"test\"}");
        var jwt = $"header.{payload}.signature";

        var result = StaticTokenCredential.TryParseJwtExpiry(jwt);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseJwtExpiry_TooFewSegments_ReturnsNull()
    {
        var result = StaticTokenCredential.TryParseJwtExpiry("only-one-segment");

        Assert.Null(result);
    }

    private static string BuildTestJwt(DateTimeOffset expiry)
    {
        var header = Base64UrlEncode("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode(JsonSerializer.Serialize(new { exp = expiry.ToUnixTimeSeconds(), sub = "test" }));
        var signature = "test-signature";
        return $"{header}.{payload}.{signature}";
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
