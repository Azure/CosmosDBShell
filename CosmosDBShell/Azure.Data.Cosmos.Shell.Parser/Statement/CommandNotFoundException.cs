// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[Serializable]
internal class CommandNotFoundException : Exception, IShellExceptionWithHint
{
    public CommandNotFoundException(string commandName)
        : this(commandName, suggestion: null)
    {
    }

    public CommandNotFoundException(string commandName, string? suggestion)
        : base(BuildMessage(commandName))
    {
        this.CommandName = commandName;
        this.Suggestion = suggestion;
        this.Hint = BuildHint(suggestion);
    }

    public string CommandName { get; }

    public string? Suggestion { get; }

    /// <inheritdoc/>
    public string? Hint { get; }

    private static string BuildMessage(string commandName)
    {
        return MessageService.GetString(
            "error-command-not-found",
            new Dictionary<string, object> { { "command", Markup.Escape(commandName) } });
    }

    private static string? BuildHint(string? suggestion)
    {
        if (string.IsNullOrEmpty(suggestion))
        {
            return null;
        }

        var hint = MessageService.GetString(
            "error-command-not-found-suggestion",
            new Dictionary<string, object> { { "suggestion", Markup.Escape(suggestion) } });

        return string.IsNullOrEmpty(hint) ? null : hint;
    }
}
