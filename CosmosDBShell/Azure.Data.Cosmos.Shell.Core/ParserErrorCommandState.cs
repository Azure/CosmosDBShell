// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Parser;

internal class ParserErrorCommandState : CommandState
{
    public ParserErrorCommandState(ErrorList errors)
    {
        this.Errors = errors;
    }

    public ErrorList Errors { get; }

    public override bool IsError => true;
}