// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Net;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;

public class ServerSideProgrammingCapabilityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnsupportedScriptExecution_IsRecognizedThroughWrappers(int wrapperCount)
    {
        Exception exception = new CosmosException(
            "Server-side script execution is not supported in this emulator",
            HttpStatusCode.BadRequest, 0, "activity", 0);
        for (var i = 0; i < wrapperCount; i++)
        {
            exception = new CommandException("sproc", exception);
        }

        Assert.True(ServerSideProgrammingTests.IsScriptExecutionUnsupported(new ErrorCommandState(exception)));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Invalid stored procedure arguments.")]
    [InlineData(HttpStatusCode.Forbidden, "Server-side script execution is not supported in this emulator")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Service unavailable.")]
    public void OtherServiceFailures_AreNotTreatedAsUnsupported(HttpStatusCode status, string message)
    {
        var exception = new CommandException("sproc", new CosmosException(message, status, 0, "activity", 0));

        Assert.False(ServerSideProgrammingTests.IsScriptExecutionUnsupported(new ErrorCommandState(exception)));
    }

    [Fact]
    public void NonCosmosFailure_IsNotTreatedAsUnsupported()
    {
        var exception = new CommandException("sproc", "Server-side script execution is not supported in this emulator");

        Assert.False(ServerSideProgrammingTests.IsScriptExecutionUnsupported(new ErrorCommandState(exception)));
    }

    [Fact]
    public void SuccessfulCommand_IsNotTreatedAsUnsupported()
    {
        Assert.False(ServerSideProgrammingTests.IsScriptExecutionUnsupported(new CommandState()));
    }
}
