// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Runtime;

using System.Net;
using System.Net.Sockets;
using System.Text.Json;

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
    public void FromException_AuthenticationFailed_ReturnsAuthFailure()
    {
        Assert.Equal(
            ShellExitCode.AuthFailure,
            ShellExitCode.FromException(new AuthenticationFailedException("no token")));
    }

    [Fact]
    public void FromException_CredentialUnavailable_ReturnsAuthFailure()
    {
        Assert.Equal(
            ShellExitCode.AuthFailure,
            ShellExitCode.FromException(new CredentialUnavailableException("no credential")));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void FromException_CosmosAuthStatus_ReturnsAuthFailure(HttpStatusCode statusCode)
    {
        var ex = new CosmosException("denied", statusCode, subStatusCode: 0, activityId: "a", requestCharge: 0);

        Assert.Equal(ShellExitCode.AuthFailure, ShellExitCode.FromException(ex));
    }

    [Theory]
    [InlineData((int)HttpStatusCode.Unauthorized)]
    [InlineData((int)HttpStatusCode.Forbidden)]
    public void FromException_RequestFailedAuthStatus_ReturnsAuthFailure(int status)
    {
        Assert.Equal(
            ShellExitCode.AuthFailure,
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

    [Fact]
    public void FromException_TimeoutException_ReturnsConnectionError()
    {
        Assert.Equal(
            ShellExitCode.ConnectionError,
            ShellExitCode.FromException(new TimeoutException("timed out")));
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

    [Theory]
    [InlineData("Cancellation Token has expired")]
    [InlineData("CosmosOperationCanceledException: request timed out")]
    [InlineData("The request timed out. See https://aka.ms/cosmosdb-tsg-request-timeout")]
    public void FromException_CosmosCancellationTimeout_ReturnsConnectionError(string message)
    {
        Assert.Equal(
            ShellExitCode.ConnectionError,
            ShellExitCode.FromException(new OperationCanceledException(message)));
    }

    [Fact]
    public void FromException_PlainOperationCanceled_ReturnsGeneralFailure()
    {
        Assert.Equal(
            ShellExitCode.GeneralFailure,
            ShellExitCode.FromException(new OperationCanceledException("operation was canceled")));
    }

    [Fact]
    public void FromException_NotFound_ReturnsNotFound()
    {
        var ex = new CosmosException("missing", HttpStatusCode.NotFound, subStatusCode: 0, activityId: "a", requestCharge: 0);

        Assert.Equal(ShellExitCode.NotFound, ShellExitCode.FromException(ex));
    }

    [Fact]
    public void FromException_TooManyRequests_ReturnsThrottled()
    {
        var ex = new CosmosException("throttled", HttpStatusCode.TooManyRequests, subStatusCode: 0, activityId: "a", requestCharge: 0);

        Assert.Equal(ShellExitCode.Throttled, ShellExitCode.FromException(ex));
    }

    [Fact]
    public void FromException_CommandNotFound_ReturnsUsageError()
    {
        Assert.Equal(
            ShellExitCode.UsageError,
            ShellExitCode.FromException(new CommandNotFoundException("nope")));
    }

    [Fact]
    public void FromException_ArgumentException_ReturnsUsageError()
    {
        Assert.Equal(
            ShellExitCode.UsageError,
            ShellExitCode.FromException(new ArgumentException("bad arg")));
    }

    [Fact]
    public void FromException_WrappedJsonException_ReturnsUsageError()
    {
        var wrapped = new CommandException("create", new JsonException("invalid JSON"));

        Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(wrapped));
    }

    [Fact]
    public void FromException_ClassifiesInnerException()
    {
        var wrapped = new Exception("wrapper", new HttpRequestException("no route"));

        Assert.Equal(ShellExitCode.ConnectionError, ShellExitCode.FromException(wrapped));
    }

    [Fact]
    public void FromException_PeelsCommandExceptionWrapper()
    {
        var wrapped = new CommandException("connect", new AuthenticationFailedException("no token"));

        Assert.Equal(ShellExitCode.AuthFailure, ShellExitCode.FromException(wrapped));
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
    public void FromCommandState_ParserError_ReturnsUsageError()
    {
        var errors = new ErrorList { ParseError.CreateError(0, 1, "unterminated string") };

        Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromCommandState(new ParserErrorCommandState(errors)));
    }

    [Fact]
    public void FromCommandState_ErrorWithAuthException_ReturnsAuthFailure()
    {
        var state = new ErrorCommandState(new RequestFailedException((int)HttpStatusCode.Forbidden, "denied"));

        Assert.Equal(ShellExitCode.AuthFailure, ShellExitCode.FromCommandState(state));
    }

    [Fact]
    public void FromCommandState_ErrorWithConnectionException_ReturnsConnectionError()
    {
        var state = new ErrorCommandState(new HttpRequestException("no route"));

        Assert.Equal(ShellExitCode.ConnectionError, ShellExitCode.FromCommandState(state));
    }

    [Fact]
    public void FromCommandState_ErrorWithGenericException_ReturnsGeneralFailure()
    {
        var state = new ErrorCommandState(new InvalidOperationException("boom"));

        Assert.Equal(ShellExitCode.GeneralFailure, ShellExitCode.FromCommandState(state));
    }
}
