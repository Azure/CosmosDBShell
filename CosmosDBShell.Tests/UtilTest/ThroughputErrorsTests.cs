// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Core;

namespace CosmosShell.Tests.UtilTest;

public class ThroughputErrorsTests
{
    [Fact]
    public void Serverless400Message_IsDetected()
    {
        const string message = "Response status code does not indicate success: BadRequest (400). Reading or replacing offers is not supported for serverless accounts.";
        Assert.True(ThroughputErrors.IsServerlessThroughputError(message));
    }

    [Fact]
    public void Detection_IsCaseInsensitive()
    {
        Assert.True(ThroughputErrors.IsServerlessThroughputError("Operation not supported for SERVERLESS account"));
    }

    [Fact]
    public void UnrelatedMessage_IsNotDetected()
    {
        Assert.False(ThroughputErrors.IsServerlessThroughputError("Owner resource does not exist"));
    }

    [Fact]
    public void NullMessage_IsNotDetected()
    {
        Assert.False(ThroughputErrors.IsServerlessThroughputError(null));
    }
}
