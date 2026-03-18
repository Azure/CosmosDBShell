// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.ArgumentParser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Parses and evaluates JSON path expressions to extract values from JSON elements.
/// </summary>
internal class JsonOperationParser
{
    private enum JSonOperationParserState
    {
        Start,
        Dot,
        ArrayStart,
        PropertyAccess,
        ArrayIndex,
    }

    /// <summary>
    /// Parses a JSON path string into a list of instructions.
    /// </summary>
    /// <param name="path">The JSON path string to parse (e.g., ".user.address.city" or ".items[0].name").</param>
    /// <returns>A list of instructions representing the parsed path.</returns>
    /// <exception cref="ArgumentException">Thrown when the path contains invalid syntax such as unclosed brackets or invalid array indices.</exception>
    public static List<JsonOperation> Parse(string path)
    {
        var instructions = new List<JsonOperation>();

        if (string.IsNullOrEmpty(path))
        {
            return instructions;
        }

        var state = JSonOperationParserState.Start;
        var currentSegment = new System.Text.StringBuilder();
        var escapeNext = false;

        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];

            // Handle escape sequences
            if (escapeNext)
            {
                // When escaped, always append the character to the current segment
                currentSegment.Append(c);
                escapeNext = false;
                continue;
            }

            if (c == '\\')
            {
                escapeNext = true;
                continue;
            }

            switch (state)
            {
                case JSonOperationParserState.Start:
                    if (c == '.')
                    {
                        state = JSonOperationParserState.Dot;
                    }
                    else if (c == '[')
                    {
                        state = JSonOperationParserState.ArrayStart;
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        // Allow paths without leading dot for backward compatibility
                        currentSegment.Append(c);
                        state = JSonOperationParserState.PropertyAccess;
                    }

                    break;

                case JSonOperationParserState.Dot:
                    if (c == '[')
                    {
                        // Direct array access after dot like .[0]
                        state = JSonOperationParserState.ArrayStart;
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        // Start of property name
                        currentSegment.Append(c);
                        state = JSonOperationParserState.PropertyAccess;
                    }

                    break;

                case JSonOperationParserState.PropertyAccess:
                    if (c == '.')
                    {
                        // End of property name, start of next
                        if (currentSegment.Length > 0)
                        {
                            instructions.Add(new PropertyAccess(currentSegment.ToString()));
                            currentSegment.Clear();
                        }

                        state = JSonOperationParserState.Dot;
                    }
                    else if (c == '[')
                    {
                        // Property followed by array access
                        if (currentSegment.Length > 0)
                        {
                            instructions.Add(new PropertyAccess(currentSegment.ToString()));
                            currentSegment.Clear();
                        }

                        state = JSonOperationParserState.ArrayStart;
                    }
                    else
                    {
                        // Continue building property name
                        currentSegment.Append(c);
                    }

                    break;

                case JSonOperationParserState.ArrayStart:
                    if (c == ']')
                    {
                        // End of array index
                        if (currentSegment.Length == 0)
                        {
                            throw new ArgumentException(MessageService.GetString("json_error_empty_array_brackets"));
                        }
                        else if (int.TryParse(currentSegment.ToString(), out var index))
                        {
                            instructions.Add(new ArrayAccess(index));
                            currentSegment.Clear();
                        }
                        else
                        {
                            throw new ArgumentException(MessageService.GetArgsString("json_error_invalid_array_index", "index", currentSegment.ToString()));
                        }

                        state = JSonOperationParserState.PropertyAccess;
                    }
                    else if (char.IsDigit(c) || (c == '-' && currentSegment.Length == 0))
                    {
                        currentSegment.Append(c);
                        state = JSonOperationParserState.ArrayIndex;
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        throw new ArgumentException(MessageService.GetArgsString("json_error_invalid_char_in_array", "char", c.ToString()));
                    }

                    break;

                case JSonOperationParserState.ArrayIndex:
                    if (c == ']')
                    {
                        if (int.TryParse(currentSegment.ToString(), out var index))
                        {
                            instructions.Add(new ArrayAccess(index));
                            currentSegment.Clear();
                        }
                        else
                        {
                            throw new ArgumentException(MessageService.GetArgsString("json_error_invalid_array_index", "index", currentSegment.ToString()));
                        }

                        state = JSonOperationParserState.PropertyAccess;
                    }
                    else if (char.IsDigit(c))
                    {
                        currentSegment.Append(c);
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        throw new ArgumentException(MessageService.GetArgsString("json_error_invalid_char_in_array", "char", c.ToString()));
                    }

                    break;
            }
        }

        // Handle any remaining content
        if (escapeNext)
        {
            throw new ArgumentException(MessageService.GetString("json_error_path_ends_with_escape"));
        }

        if (currentSegment.Length > 0)
        {
            switch (state)
            {
                case JSonOperationParserState.PropertyAccess:
                    instructions.Add(new PropertyAccess(currentSegment.ToString()));
                    break;
                case JSonOperationParserState.ArrayIndex:
                case JSonOperationParserState.ArrayStart:
                    throw new ArgumentException(MessageService.GetString("json_error_unclosed_array_bracket"));
            }
        }

        return instructions;
    }

    /// <summary>
    /// Evaluates a JSON path against a JsonElement, supporting pipe operations.
    /// </summary>
    /// <param name="argument">The JSON path string, which may contain pipe operators.</param>
    /// <returns>The JsonElement at the specified path.</returns>
    /// <exception cref="PropertyNotFoundException">Thrown when a specified property is not found.</exception>
    /// <exception cref="ElementIsNoArrayException">Thrown when attempting array access on a non-array element.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown when an array index is out of bounds.</exception>
    public static JsonElement Evaluate(ShellInterpreter interpreter, CommandState state, string argument)
    {
        // Split by pipe operator, but not within brackets
        var segments = SplitByPipe(argument);
        var evaluatedResult = state.Result?.ConvertShellObject(Parser.DataType.Json);
        if (evaluatedResult == null)
        {
            throw new InvalidOperationException(MessageService.GetString("json_error_result_evaluation_null"));
        }

        var current = (JsonElement)evaluatedResult;

        foreach (var segment in segments)
        {
            var trimmedSegment = segment.Trim();
            if (string.IsNullOrEmpty(trimmedSegment))
            {
                continue;
            }

            var instructions = Parse(trimmedSegment);
            foreach (var instruction in instructions)
            {
                current = instruction.Evaluate(current);
            }
        }

        return current;
    }

    /// <summary>
    /// Splits a path by pipe operators, respecting bracket boundaries.
    /// </summary>
    private static List<string> SplitByPipe(string path)
    {
        var segments = new List<string>();
        var currentSegment = new System.Text.StringBuilder();
        var inBracket = false;
        var inQuote = false;
        var escapeNext = false;

        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];

            if (escapeNext)
            {
                currentSegment.Append(c);
                escapeNext = false;
                continue;
            }

            if (c == '\\')
            {
                escapeNext = true;

                // Keep the backslash in the output for the parser to handle
                currentSegment.Append(c);
                continue;
            }

            if (c == '"' && !inBracket)
            {
                inQuote = !inQuote;
                currentSegment.Append(c);
                continue;
            }

            if (!inQuote)
            {
                if (c == '[')
                {
                    inBracket = true;
                }
                else if (c == ']')
                {
                    inBracket = false;
                }
                else if (c == '|' && !inBracket)
                {
                    // Found a pipe outside of brackets
                    segments.Add(currentSegment.ToString());
                    currentSegment.Clear();
                    continue;
                }
            }

            currentSegment.Append(c);
        }

        // Add the last segment
        if (currentSegment.Length > 0)
        {
            segments.Add(currentSegment.ToString());
        }

        return segments;
    }
}