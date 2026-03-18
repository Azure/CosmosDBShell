// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[Serializable]
internal class CommandNotFoundException : Exception
{
    public CommandNotFoundException(string commandName)
        : base(MessageService.GetString(
            "error-command-not-found",
            new Dictionary<string, object> { { "command", Markup.Escape(commandName) } }))
    {
        this.CommandName = commandName;
    }

    public string CommandName { get; }
}