// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Net;
using System.Net.Sockets;

using Azure;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Identity;

using Microsoft.Azure.Cosmos;

using Xunit;

public class ShellExitCodeTests
{
    [Fact]
    public void FromException_Null_ReturnsGeneralFailure()
    {
        Assert.Equal(ShellExitCode.GeneralFailure, ShellExitCode.FromException(null));
    }

    [Fact]
    public void FromException_UnrelatedException_ReturnsGeneralFailure()
    {
        Assert.Equal(ShellExitCode.GeneralFailure, ShellExitCode.FromException(new InvalidOperationException("boom")));
    }

    [Fact]
    public void FromException_AuthenticationFailed_ReturnsAuthenticationFailure()
    {
        Assert.Equal(
            ShellExitCode.AuthenticationFailure,
            ShellExitCode.FromException(new AuthenticationFailedException("no token")));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void FromException_CosmosAuthStatus_ReturnsAuthenticationFailure(HttpStatusCode statusCode)
    {
        var ex = new CosmosException("denied", statusCode, subStatusCode: 0, activityId: "a", requestCharge: 0);

        Assert.Equal(ShellExitCode.AuthenticationFailure, ShellExitCode.FromException(ex));
    }

    [Theory]
    [InlineData((int)HttpStatusCode.Unauthorized)]
    [InlineData((int)HttpStatusCode.Forbidden)]
    public void FromException_RequestFailedAuthStatus_ReturnsAuthenticationFailure(int status)
    {
        Assert.Equal(
            ShellExitCode.AuthenticationFailure,
            ShellExitCode.FromException(new RequestFailedException(status, "denied")));
    }

    [Fact]
    public void FromException_HttpRequestException_ReturnsConnectionError()
    {
        Assert.Equal(
            ShellExitCode.ConnectionError,
            ShellExitCode.FromException(new HttpRequestException("no route")));
    }

    [Fact]
    public void FromException_SocketException_ReturnsConnectionError()
    {
        Assert.Equal(
            ShellExitCode.ConnectionError,
            ShellExitCode.FromException(new SocketException((int)SocketError.ConnectionRefused)));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void FromException_CosmosConnectivityStatus_ReturnsConnectionError(HttpStatusCode statusCode)
    {
        var ex = new CosmosException("unavailable", statusCode, subStatusCode: 0, activityId: "a", requestCharge: 0);

        Assert.Equal(ShellExitCode.ConnectionError, ShellExitCode.FromException(ex));
    }

    [Theory]
    [InlineData((int)HttpStatusCode.ServiceUnavailable)]
    [InlineData((int)HttpStatusCode.RequestTimeout)]
    [InlineData((int)HttpStatusCode.GatewayTimeout)]
    public void FromException_RequestFailedConnectivityStatus_ReturnsConnectionError(int status)
    {
        Assert.Equal(
            ShellExitCode.ConnectionError,
            ShellExitCode.FromException(new RequestFailedException(status, "unavailable")));
    }

    [Fact]
    public void FromException_ClassifiesInnerException()
    {
        var wrapped = new Exception("wrapper", new HttpRequestException("no route"));

        Assert.Equal(ShellExitCode.ConnectionError, ShellExitCode.FromException(wrapped));
    }

    [Fact]
    public void FromCommandState_Null_ReturnsSuccess()
    {
        Assert.Equal(ShellExitCode.Success, ShellExitCode.FromCommandState(null));
    }

    [Fact]
    public void FromCommandState_NonError_ReturnsSuccess()
    {
        Assert.Equal(ShellExitCode.Success, ShellExitCode.FromCommandState(new CommandState()));
    }

    [Fact]
    public void FromCommandState_ParserError_ReturnsSyntaxError()
    {
        var errors = new ErrorList { ParseError.CreateError(0, 1, "unterminated string") };

        Assert.Equal(ShellExitCode.SyntaxError, ShellExitCode.FromCommandState(new ParserErrorCommandState(errors)));
    }

    [Fact]
    public void FromCommandState_ErrorWithAuthException_ReturnsAuthenticationFailure()
    {
        var state = new ErrorCommandState(new RequestFailedException((int)HttpStatusCode.Forbidden, "denied"));

        Assert.Equal(ShellExitCode.AuthenticationFailure, ShellExitCode.FromCommandState(state));
    }

    [Fact]
    public void FromCommandState_ErrorWithGenericException_ReturnsGeneralFailure()
    {
        var state = new ErrorCommandState(new InvalidOperationException("boom"));

        Assert.Equal(ShellExitCode.GeneralFailure, ShellExitCode.FromCommandState(state));
    }
}
