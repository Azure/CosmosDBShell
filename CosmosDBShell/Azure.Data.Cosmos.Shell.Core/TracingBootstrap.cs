// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

/// <summary>
/// Owns the distributed-tracing lifecycle for the shell. When enabled it registers
/// a <see cref="TracerProvider"/> that records every activity, which causes the
/// Azure Cosmos DB SDK to emit a sampled W3C <c>traceparent</c>
/// (the <c>-01</c> flag) on its outgoing requests. An OTLP exporter is added only
/// when an endpoint is supplied.
/// </summary>
public sealed class TracingBootstrap : IDisposable
{
    /// <summary>
    /// Name of the <see cref="ActivitySource"/> used for per-command root activities.
    /// </summary>
    public const string ActivitySourceName = "CosmosDBShell";

    private const string CosmosOperationSourceName = "Azure.Cosmos.Operation";

    private static readonly ActivitySource SharedSource = new(ActivitySourceName);

    private readonly TracerProvider provider;

    private TracingBootstrap(TracerProvider provider)
    {
        this.provider = provider;
    }

    /// <summary>
    /// Enables distributed tracing for the current process. Sets the Azure SDK
    /// experimental switch required to emit <c>traceparent</c> headers and builds a
    /// tracer provider that records all activities.
    /// </summary>
    /// <param name="otlpEndpoint">Optional OTLP endpoint to export spans to. When null or empty, no exporter is added and tracing only propagates a sampled <c>traceparent</c> on the wire.</param>
    /// <returns>A <see cref="TracingBootstrap"/> that must be disposed to flush and tear down the provider.</returns>
    public static TracingBootstrap Initialize(string? otlpEndpoint)
    {
        // Required so the Azure.Core HTTP pipeline writes a W3C traceparent header.
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

        var builder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ActivitySourceName))
            .SetSampler(new AlwaysOnSampler())
            .AddSource(CosmosOperationSourceName)
            .AddSource(ActivitySourceName);

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }

        return new TracingBootstrap(builder.Build());
    }

    /// <summary>
    /// Starts a root activity for a shell command. Returns null when tracing is not
    /// enabled, so callers incur no overhead in the common case.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <returns>The started activity, or null when no tracer is listening.</returns>
    public static Activity? StartCommandActivity(string name)
    {
        return SharedSource.StartActivity(name, ActivityKind.Client);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.provider.Dispose();
    }
}
