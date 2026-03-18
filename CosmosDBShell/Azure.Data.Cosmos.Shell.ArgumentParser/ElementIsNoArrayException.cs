// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.ArgumentParser;

/// <summary>
/// Exception thrown when attempting to access an array element on a JSON element that is not an array.
/// </summary>
public class ElementIsNoArrayException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ElementIsNoArrayException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ElementIsNoArrayException(string message)
        : base(message)
    {
    }
}
