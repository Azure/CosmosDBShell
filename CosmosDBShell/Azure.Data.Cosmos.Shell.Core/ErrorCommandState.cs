// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Util;

internal class ErrorCommandState(Exception exception) : CommandState
{
    public Exception Exception { get; init; } = exception;

    public override bool IsError => true;

    public override int ExitCode => ShellExitCode.FromException(this.Exception);
}
