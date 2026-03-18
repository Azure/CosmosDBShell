// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Exception for commands that require a selected database.
/// </summary>
public sealed class NotInDatabaseException : CommandException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotInDatabaseException"/> class.
    /// </summary>
    /// <param name="command">The command that requires a selected database.</param>
    public NotInDatabaseException(string command)
        : base(command, MessageService.GetString("error-not_inside_database"))
    {
    }
}
