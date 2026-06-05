// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

[Serializable]
internal class CommandNotFoundException : Exception, IShellExceptionWithHint
{
    public CommandNotFoundException(string commandName)
        : this(commandName, suggestion: null)
    {
    }

    public CommandNotFoundException(string commandName, string? suggestion)
        : this(commandName, suggestion, start: null, length: null)
    {
    }

    public CommandNotFoundException(string commandName, string? suggestion, int? start, int? length)
        : base(BuildMessage(commandName))
    {
        this.CommandName = commandName;
        this.Suggestion = suggestion;
        this.Hint = BuildHint(suggestion);
        this.Start = start;
        this.Length = length;
    }

    public string CommandName { get; }

    public string? Suggestion { get; }

    /// <summary>
    /// Gets the offset of the unrecognized command name within the source text,
    /// when known. Used to render a source-caret diagnostic.
    /// </summary>
    public int? Start { get; }

    /// <summary>
    /// Gets the length of the unrecognized command name, when known.
    /// </summary>
    public int? Length { get; }

    /// <inheritdoc/>
    public string? Hint { get; }

    private static string BuildMessage(string commandName)
    {
        // The command name is stored unescaped; each display path escapes it
        // for Spectre markup at render time (mirroring UnknownOptionException).
        return MessageService.GetString(
            "error-command-not-found",
            new Dictionary<string, object> { { "command", commandName } });
    }

    private static string? BuildHint(string? suggestion)
    {
        if (string.IsNullOrEmpty(suggestion))
        {
            return null;
        }

        var hint = MessageService.GetString(
            "error-command-not-found-suggestion",
            new Dictionary<string, object> { { "suggestion", suggestion } });

        return string.IsNullOrEmpty(hint) ? null : hint;
    }
}
