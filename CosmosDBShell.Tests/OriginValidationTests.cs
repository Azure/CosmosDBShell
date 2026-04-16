// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Mcp;

namespace CosmosShell.Tests;

public class OriginValidationTests
{
    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:6128")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:6128")]
    [InlineData("https://localhost")]
    [InlineData("https://localhost:3000")]
    [InlineData("http://[::1]")]
    [InlineData("http://[::1]:6128")]
    public void AllowsLoopbackOrigins(string origin)
    {
        Assert.True(OriginValidationMiddleware.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("http://evil.com")]
    [InlineData("https://attacker.example.com")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://example.com:6128")]
    [InlineData("http://localhost.evil.com")]
    [InlineData("ftp://localhost")]
    [InlineData("file://localhost")]
    [InlineData("http://user@localhost")]
    public void RejectsNonLoopbackOrigins(string origin)
    {
        Assert.False(OriginValidationMiddleware.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("://missing-scheme")]
    public void RejectsMalformedOrigins(string origin)
    {
        Assert.False(OriginValidationMiddleware.IsAllowedOrigin(origin));
    }
}
