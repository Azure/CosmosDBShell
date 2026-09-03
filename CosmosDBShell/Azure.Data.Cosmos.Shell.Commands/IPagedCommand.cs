//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

/// <summary>
/// Implemented by commands that return bounded, resumable pages when invoked through MCP.
/// Both members are set by the MCP layer only and are deliberately not shell options.
/// </summary>
internal interface IPagedCommand
{
    bool IsMcpRequest { get; set; }

    string? Continuation { get; set; }
}
