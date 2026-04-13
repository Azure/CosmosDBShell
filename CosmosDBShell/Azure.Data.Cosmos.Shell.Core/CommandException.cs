// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Represents errors that occur during command execution in the Cosmos Shell.
/// </summary>
public class CommandException : ShellException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandException"/> class with a specified command and innerException.
    /// </summary>
    /// <param name="command">The command that caused the innerException.</param>
    /// <param name="innerException">The innerException that occurred.</param>
    public CommandException(string command, Exception innerException)
        : base(GetMessage(innerException), innerException)
    {
        this.Command = command;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandException"/> class with a specified command and error message.
    /// </summary>
    /// <param name="command">The command that caused the innerException.</param>
    /// <param name="message">The error message.</param>
    public CommandException(string command, string message)
        : base(message)
    {
        this.Command = command;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandException"/> class with a specified command and error message.
    /// </summary>
    /// <param name="command">The command that caused the innerException.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or null if no inner exception is specified.</param>
    public CommandException(string command, string message, Exception? innerException)
        : base(message, innerException)
    {
        this.Command = command;
    }

    /// <summary>
    /// Gets the command that caused the innerException.
    /// </summary>
    public string Command { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.Command}: {base.ToString()}";
    }

    private static string GetMessage(Exception exception)
    {
        if (exception.InnerException != null)
        {
            return string.Format("{0} ({1})", exception.Message, exception.InnerException.Message);
        }

        return exception.Message;
    }
}
