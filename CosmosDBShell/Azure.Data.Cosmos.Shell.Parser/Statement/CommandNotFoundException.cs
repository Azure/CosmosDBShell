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
        : this(commandName, suggestion: null)
    {
    }

    public CommandNotFoundException(string commandName, string? suggestion)
        : base(BuildMessage(commandName, suggestion))
    {
        this.CommandName = commandName;
        this.Suggestion = suggestion;
    }

    public string CommandName { get; }

    public string? Suggestion { get; }

    private static string BuildMessage(string commandName, string? suggestion)
    {
        var baseMessage = MessageService.GetString(
            "error-command-not-found",
            new Dictionary<string, object> { { "command", Markup.Escape(commandName) } });

        if (string.IsNullOrEmpty(suggestion))
        {
            return baseMessage;
        }

        var hint = MessageService.GetString(
            "error-command-not-found-suggestion",
            new Dictionary<string, object> { { "suggestion", Markup.Escape(suggestion) } });

        return string.IsNullOrEmpty(hint) ? baseMessage : baseMessage + " " + hint;
    }
}
