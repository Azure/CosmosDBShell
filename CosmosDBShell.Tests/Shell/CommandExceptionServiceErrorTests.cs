// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;

public class CommandExceptionServiceErrorTests
{
    internal const string ThroughputMessage =
        "Your account is currently configured with a total throughput limit of 10000 RU/s. " +
        "This operation failed because it would have increased the total throughput to 11000 RU/s. " +
        "See https://aka.ms/cosmos-tp-limit for more information.";

    internal static RequestFailedException CreateThroughputException()
    {
        var inner = JsonSerializer.Serialize(new { Errors = new[] { ThroughputMessage } });
        var outer = JsonSerializer.Serialize(new
        {
            code = "BadRequest",
            message = $"Message: {inner}\r\nActivityId: activity-id, Request URI: /internal/replicas, SDK: internal-sdk",
        });
        return new RequestFailedException(
            200,
            $"Message: {outer}, Request URI: /dbs/test/colls\r\nStatus: 200 (Ok)\r\nErrorCode: BadRequest",
            "BadRequest",
            null);
    }

    [Fact]
    public void ArmNestedThroughputError_ExtractsActionableMessageAndRetainsOriginal()
    {
        var original = CreateThroughputException();
        var exception = new CommandException("mkcon", original);

        Assert.Equal(ThroughputMessage, exception.Message);
        Assert.Equal(ThroughputMessage, CommandException.GetDisplayMessage(exception));
        Assert.Same(original, exception.InnerException);
        Assert.Contains("/internal/replicas", exception.ToString());
        Assert.Equal(ShellExitCode.GeneralFailure, ShellExitCode.FromException(exception));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CosmosErrorsArray_ExtractsAllErrors(bool dataPlane)
    {
        const string message = "Message: {\"Errors\":[\"First [error]\", \"Second error\"]}\r\nActivityId: diagnostic";
        Exception original = dataPlane
            ? new CosmosException(message, HttpStatusCode.BadRequest, 0, "activity", 3.5)
            : new RequestFailedException(400, message);

        Assert.Equal($"First [error]{Environment.NewLine}Second error", CommandException.GetDisplayMessage(original));
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"Forbidden\",\"message\":\"Access denied.\"}}", "Access denied.")]
    [InlineData("{\"code\":\"BadRequest\",\"message\":\"Invalid partition key.\"}", "Invalid partition key.")]
    [InlineData("{\"error\":{\"message\":{\"Errors\":[\"Limit exceeded.\"]}}}", "Limit exceeded.")]
    public void StandardAzureErrors_ExtractServiceMessage(string payload, string expected)
    {
        Assert.Equal(expected, CommandException.GetDisplayMessage(new RequestFailedException(400, payload)));
    }

    [Theory]
    [InlineData("Connection refused.")]
    [InlineData("Message: {malformed\r\nActivityId: keep diagnostic")]
    [InlineData("{\"unknown\":\"keep diagnostic\"}")]
    [InlineData("{\"Errors\":[]}")]
    [InlineData("{\"message\":null}")]
    public void UnrecognizedMessages_PreserveOriginal(string message)
    {
        var exception = new RequestFailedException(400, message);
        Assert.Equal(exception.Message, CommandException.GetDisplayMessage(exception));
    }

    [Fact]
    public void ExcessiveNesting_DoesNotRecurseIndefinitely()
    {
        var payload = "{\"message\":\"failure\"}";
        for (var i = 0; i < 40; i++)
        {
            payload = "{\"error\":" + payload + "}";
        }

        var exception = new RequestFailedException(400, payload);
        Assert.Equal(exception.Message, CommandException.GetDisplayMessage(exception));
    }

    [Fact]
    public void ExplicitShellMessage_SummaryIsNotReplacedByServiceDiagnostics()
    {
        var exception = new CommandException("mkcon", "Creation failed.", CreateThroughputException());
        Assert.Equal("Creation failed.", CommandException.GetSummaryMessage(exception));
        Assert.Equal($"Creation failed. ({ThroughputMessage})", CommandException.GetDisplayMessage(exception));
    }

    [Fact]
    public void WrappedServiceError_DisplayDoesNotRepeatMessage()
    {
        var exception = new CommandException("mkcon", CreateThroughputException());
        Assert.Equal(ThroughputMessage, CommandException.GetSummaryMessage(exception));
        Assert.Equal(ThroughputMessage, CommandException.GetDisplayMessage(exception));
    }
}
