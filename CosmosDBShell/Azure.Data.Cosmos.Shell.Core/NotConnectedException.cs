// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Exception for commands that require an account connection.
/// </summary>
public sealed class NotConnectedException : CommandException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotConnectedException"/> class.
    /// </summary>
    /// <param name="command">The command that requires an account connection.</param>
    public NotConnectedException(string command)
        : base(command, MessageService.GetString("error-not_connected_account"))
    {
    }
}
