// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;
using System.Globalization;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Shared option-value conversion used by <see cref="CommandStatement"/> and
/// <see cref="CommandExpression"/> so typed command options (bool, enum, int, double, ...)
/// are parsed and validated consistently regardless of whether a command is invoked
/// as a statement or as an expression.
/// </summary>
internal static class CommandOptionBinder
{
    /// <summary>
    /// Converts the string form of a command option value to <paramref name="targetType"/>,
    /// applying the same parsing rules in all invocation contexts.
    /// </summary>
    /// <param name="commandName">Command name used in error messages.</param>
    /// <param name="rawName">Option name (without leading dashes) used in error messages.</param>
    /// <param name="stringValue">Raw string value parsed from the command line.</param>
    /// <param name="targetType">Target property type (already unwrapped from <see cref="Nullable{T}"/>).</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="CommandException">Thrown when the value cannot be converted.</exception>
    public static object ConvertOptionValue(string commandName, string rawName, string stringValue, Type targetType)
    {
        if (targetType == typeof(bool))
        {
            if (string.Equals(stringValue, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(stringValue, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new CommandException(commandName, $"Invalid boolean value '{stringValue}' for option '{rawName}'. Expected 'true' or 'false'.");
        }

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, stringValue, ignoreCase: true, out var enumVal))
            {
                return enumVal!;
            }

            var validValues = string.Join(", ", Enum.GetNames(targetType));
            throw new CommandException(commandName, $"Invalid value '{stringValue}' for option '{rawName}'. Valid values are: {validValues}");
        }

        if (targetType == typeof(int))
        {
            if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
            {
                return intVal;
            }

            throw new CommandException(commandName, $"Invalid integer value '{stringValue}' for option '{rawName}'.");
        }

        if (targetType == typeof(double))
        {
            if (double.TryParse(stringValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dblVal))
            {
                return dblVal;
            }

            throw new CommandException(commandName, $"Invalid numeric value '{stringValue}' for option '{rawName}'.");
        }

        try
        {
            return Convert.ChangeType(stringValue, targetType, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            throw new CommandException(commandName, $"Invalid value '{stringValue}' for option '{rawName}'. Expected a value of type '{targetType.Name}'.", ex);
        }
    }
}
