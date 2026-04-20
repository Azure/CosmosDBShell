// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

[AttributeUsage(AttributeTargets.Class)]
internal class CosmosCommandAttribute : Attribute
{
    public CosmosCommandAttribute(string commandName)
    {
        this.CommandName = commandName;
    }

    /// <summary>
    /// Gets the command name for shell execute.
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    /// Gets or sets alternate names that can invoke the same command.
    /// </summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether this command is an external shell command.
    /// </summary>
    public bool External { get; set; }
}
