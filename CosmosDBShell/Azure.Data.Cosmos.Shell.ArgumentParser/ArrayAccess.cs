// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.ArgumentParser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Represents an instruction to access an array element at a specific index in a JSON structure.
/// </summary>
/// <param name="index">The zero-based index of the array element to access.</param>
internal class ArrayAccess(int index) : JsonOperation
{
    /// <summary>
    /// Gets the index of the array element to access.
    /// </summary>
    public int Index => index;

    /// <summary>
    /// Evaluates the array access instruction against a JSON element.
    /// </summary>
    /// <param name="element">The JSON element to evaluate against.</param>
    /// <returns>The JSON element at the specified array index.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when the index is negative or greater than or equal to the array length.</exception>
    /// <exception cref="ElementIsNoArrayException">Thrown when the element is not a JSON array.</exception>
    public override JsonElement Evaluate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            if (this.Index < 0 || this.Index >= element.GetArrayLength())
            {
                throw new IndexOutOfRangeException(MessageService.GetArgsString("json_error_array_index_out_of_bounds", "index", this.Index, "length", element.GetArrayLength()));
            }

            return element[this.Index];
        }

        throw new ElementIsNoArrayException(MessageService.GetString("json_error_no_array"));
    }
}
