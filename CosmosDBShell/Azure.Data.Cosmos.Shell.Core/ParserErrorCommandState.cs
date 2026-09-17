// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Parser;

internal class ParserErrorCommandState : CommandState
{
    public ParserErrorCommandState(ErrorList errors, string? sourceName = null, string? sourceText = null)
    {
        this.Errors = errors;
        this.SourceName = sourceName;
        this.SourceText = sourceText;
    }

    public ErrorList Errors { get; init; }

    public string? SourceName { get; }

    public string? SourceText { get; }

    public override bool IsError => true;

    public override int ExitCode => ShellExitCode.UsageError;
}
