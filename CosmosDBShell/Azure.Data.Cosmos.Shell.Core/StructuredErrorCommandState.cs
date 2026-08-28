// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Parser;

internal sealed class StructuredErrorCommandState : ErrorCommandState
{
    public StructuredErrorCommandState(Exception exception, ShellObject result)
        : base(exception)
    {
        this.Result = result;
    }
}