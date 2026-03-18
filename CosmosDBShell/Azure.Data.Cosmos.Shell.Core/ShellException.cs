// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents errors that occur during shell execution in the Cosmos DB Shell environment.
/// </summary>
public class ShellException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShellException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ShellException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or null if no inner exception is specified.</param>
    public ShellException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
