// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

[AttributeUsage(AttributeTargets.Class)]
internal class McpAnnotationAttribute : Attribute
{
    public McpAnnotationAttribute()
    {
    }

    /// <summary>
    /// Gets or sets a human-readable title for the tool.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the description for MCP.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this command is restricted in MCP.
    /// </summary>
    public bool Restricted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool does not modify its environment.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool may perform destructive updates.
    /// </summary>
    public bool Destructive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether repeated calls with same arguments have no additional effect.
    /// </summary>
    public bool Idempotent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool interacts with external entities.
    /// </summary>
    public bool OpenWorld { get; set; }
}
