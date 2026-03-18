// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.ArgumentParser;

using System.Text.Json;

/// <summary>
/// Exception thrown when a specified property is not found in a JSON element.
/// </summary>
public class PropertyNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyNotFoundException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PropertyNotFoundException(string message)
        : base(message)
    {
    }
}