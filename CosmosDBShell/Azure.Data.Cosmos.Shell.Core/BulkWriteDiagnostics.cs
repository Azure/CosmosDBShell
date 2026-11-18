// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Collects per-item bulk-write failures for the single machine-mode error payload.
/// Count and length are bounded so large failing writes do not retain every diagnostic.
/// </summary>
internal sealed class BulkWriteDiagnostics
{
    internal const int MaximumCount = 100;
    internal const int MaximumLength = 2048;

    private readonly List<string> errors = [];
    private long omittedCount;

    internal int Count => this.errors.Count;

    internal void Add(string error)
    {
        if (this.errors.Count >= MaximumCount)
        {
            this.omittedCount++;
            return;
        }

        if (error.Length > MaximumLength)
        {
            var marker = MessageService.GetString("error-bulk-diagnostic_truncated");
            error = error[..(MaximumLength - marker.Length)] + marker;
        }

        this.errors.Add(error);
    }

    internal string GetMessage()
    {
        var message = string.Join(Environment.NewLine, this.errors);
        if (this.omittedCount > 0)
        {
            message += Environment.NewLine + MessageService.GetArgsString(
                "error-bulk-diagnostics_omitted",
                "count",
                this.omittedCount);
        }

        return message;
    }
}
