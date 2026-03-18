// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Exception for commands that require a selected container.
/// </summary>
public sealed class NotInContainerException : CommandException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotInContainerException"/> class.
    /// </summary>
    /// <param name="command">The command that requires a selected container.</param>
    public NotInContainerException(string command)
        : base(command, MessageService.GetString("error-not_inside_container"))
    {
    }
}
