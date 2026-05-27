// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Marker subclass that signals a command has already rendered its own
/// user-facing diagnostic (line/column/caret view, etc.) and that the
/// default error reporter should stay quiet to avoid double-printing.
/// State propagation (IsError, history, exit code) is unchanged.
/// </summary>
public sealed class CommandReportedException : CommandException
{
    public CommandReportedException(string command, Exception innerException)
        : base(command, innerException)
    {
    }

    public CommandReportedException(string command, string message)
        : base(command, message)
    {
    }

    public CommandReportedException(string command, string message, Exception? innerException)
        : base(command, message, innerException)
    {
    }
}
