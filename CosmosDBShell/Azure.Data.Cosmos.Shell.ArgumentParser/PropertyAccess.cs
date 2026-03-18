// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.ArgumentParser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Represents an instruction to access a property by name in a JSON object.
/// </summary>
/// <param name="propertyName">The name of the property to access.</param>
internal class PropertyAccess(string propertyName) : JsonOperation
{
    /// <summary>
    /// Gets the name of the property to access.
    /// </summary>
    public string PropertyName => propertyName;

    /// <summary>
    /// Evaluates the property access instruction against a JSON element.
    /// </summary>
    /// <param name="element">The JSON element to evaluate against.</param>
    /// <returns>The value of the specified property.</returns>
    /// <exception cref="PropertyNotFoundException">Thrown when the specified property is not found in the JSON object.</exception>
    public override JsonElement Evaluate(JsonElement element)
    {
        if (element.TryGetProperty(this.PropertyName, out JsonElement propertyValue))
        {
            return propertyValue;
        }

        throw new PropertyNotFoundException(MessageService.GetArgsString("json_error_property_not_found", "property", propertyName));
    }
}
