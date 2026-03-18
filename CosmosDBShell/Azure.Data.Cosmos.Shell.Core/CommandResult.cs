// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Util;

internal class CommandResult
{
    public CommandResult()
    {
    }

    public JsonDocument? Output { get; set; }
}
