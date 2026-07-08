// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Unit tests for <see cref="JwtClaims"/>, the JWT payload reader used by the
/// <c>whoami</c> command. The helper decodes claims for local introspection only
/// and never validates the token signature.
/// </summary>
public class JwtClaimsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    [InlineData("a.!!!.c")]
    public void TryDecodePayload_MalformedToken_ReturnsNull(string? token)
    {
        Assert.Null(JwtClaims.TryDecodePayload(token));
    }

    [Fact]
    public void TryDecodePayload_ValidToken_ReturnsClaims()
    {
        var token = BuildToken("{\"oid\":\"abc\",\"tid\":\"tenant\"}");

        var claims = JwtClaims.TryDecodePayload(token);

        Assert.NotNull(claims);
        Assert.Equal("abc", JwtClaims.GetString(claims, "oid"));
        Assert.Equal("tenant", JwtClaims.GetString(claims, "tid"));
    }

    [Fact]
    public void GetString_TriesCandidatesInOrder()
    {
        var token = BuildToken("{\"preferred_username\":\"user@contoso.com\"}");
        var claims = JwtClaims.TryDecodePayload(token);

        Assert.Equal(
            "user@contoso.com",
            JwtClaims.GetString(claims, "upn", "preferred_username", "unique_name"));
    }

    [Fact]
    public void GetString_MissingClaim_ReturnsNull()
    {
        var token = BuildToken("{\"oid\":\"abc\"}");
        var claims = JwtClaims.TryDecodePayload(token);

        Assert.Null(JwtClaims.GetString(claims, "upn"));
    }

    [Fact]
    public void GetString_NonStringClaim_ReturnsNull()
    {
        var token = BuildToken("{\"oid\":42}");
        var claims = JwtClaims.TryDecodePayload(token);

        Assert.Null(JwtClaims.GetString(claims, "oid"));
    }

    private static string BuildToken(string payloadJson)
    {
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        return $"header.{payload}.signature";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
