// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Diagnostics;

using Azure.Data.Cosmos.Shell.Core;

using Xunit;

public class TracingBootstrapTests
{
    [Fact]
    public void StartCommandActivity_WithoutProvider_ReturnsNull()
    {
        using var activity = TracingBootstrap.StartCommandActivity("cosmosdbshell.command");

        Assert.Null(activity);
    }

    [Fact]
    public void StartCommandActivity_WhenInitialized_ProducesRecordedActivity()
    {
        using var tracing = TracingBootstrap.Initialize(otlpEndpoint: null);

        using var activity = TracingBootstrap.StartCommandActivity("cosmosdbshell.command");

        Assert.NotNull(activity);
        Assert.True(activity!.Recorded);
        Assert.True(activity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded));
    }

    [Fact]
    public void Initialize_SetsAzureActivitySourceSwitch()
    {
        using var tracing = TracingBootstrap.Initialize(otlpEndpoint: null);

        Assert.True(
            AppContext.TryGetSwitch("Azure.Experimental.EnableActivitySource", out var enabled) && enabled);
    }
}
