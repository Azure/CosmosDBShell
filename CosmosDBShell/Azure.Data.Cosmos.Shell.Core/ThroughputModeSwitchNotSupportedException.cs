// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Thrown when a throughput write would switch a resource between manual and
/// autoscale, but the active connection cannot perform that migration. The Cosmos
/// data-plane SDK can only change the value within the current mode; switching
/// modes requires a control-plane (ARM/AAD) connection, the Azure portal, CLI, or
/// PowerShell.
/// </summary>
internal sealed class ThroughputModeSwitchNotSupportedException : System.InvalidOperationException
{
    public ThroughputModeSwitchNotSupportedException(string resourceName, bool targetIsAutoscale, System.Exception? innerException = null)
        : base($"Switching throughput mode for '{resourceName}' is not supported on this connection.", innerException)
    {
        this.ResourceName = resourceName;
        this.TargetIsAutoscale = targetIsAutoscale;
    }

    public string ResourceName { get; }

    public bool TargetIsAutoscale { get; }
}
