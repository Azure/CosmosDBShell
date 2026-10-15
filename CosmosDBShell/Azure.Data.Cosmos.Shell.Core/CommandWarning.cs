// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal sealed class CommandWarning(string message)
{
    public string Message { get; set; } = message;

    public bool IsRendered { get; set; }
}
