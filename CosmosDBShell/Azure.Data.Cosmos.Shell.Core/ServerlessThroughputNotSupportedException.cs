// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Thrown when <c>--scale</c> or <c>--ru</c> is supplied while creating a database or
/// container on a serverless account, which cannot have provisioned throughput.
/// </summary>
internal sealed class ServerlessThroughputNotSupportedException : System.InvalidOperationException
{
    public ServerlessThroughputNotSupportedException(System.Exception? innerException = null)
        : base("Provisioned throughput is not supported on a serverless account.", innerException)
    {
    }
}
