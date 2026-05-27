// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents an "unknown option" error raised while binding command
/// arguments. Carries an optional <see cref="Hint"/> sentence
/// ("Did you mean '--foo'?") so the shell can render it on a second,
/// non-error-colored line.
/// </summary>
public class UnknownOptionException : CommandException, IShellExceptionWithHint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownOptionException"/> class.
    /// </summary>
    /// <param name="command">The command that received the unknown option.</param>
    /// <param name="message">The base error message (no hint appended).</param>
    /// <param name="hint">An optional formatted "Did you mean 'X'?" hint.</param>
    public UnknownOptionException(string command, string message, string? hint)
        : base(command, message)
    {
        this.Hint = hint;
    }

    /// <inheritdoc/>
    public string? Hint { get; }
}
