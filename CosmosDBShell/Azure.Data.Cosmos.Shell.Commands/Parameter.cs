//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Reflection;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

/// <summary>
/// Represents a parameter for a Cosmos command, encapsulating its metadata and providing methods for value assignment and validation.
/// </summary>
/// <param name="p">The <see cref="PropertyInfo"/> representing the property associated with this parameter.</param>
/// <param name="pAttr">The <see cref="CosmosParameterAttribute"/> containing metadata for the parameter.</param>
internal class Parameter(PropertyInfo p, CosmosParameterAttribute pAttr)
{
    private readonly PropertyInfo p = p;
    private readonly CosmosParameterAttribute pAttr = pAttr;

    /// <summary>
    /// Gets the <see cref="PropertyInfo"/> associated with this parameter.
    /// </summary>
    public PropertyInfo PropertyInfo { get => this.p; }

    /// <summary>
    /// Gets the names associated with this parameter.
    /// </summary>
    public string[] Name { get => this.pAttr.Name; }

    /// <summary>
    /// Gets a value indicating whether the parameter is required.
    /// </summary>
    /// <value>
    /// <c>true</c> if the parameter is required; otherwise, <c>false</c>.
    /// </value>
    public bool IsRequired { get => this.pAttr.IsRequired; }

    /// <summary>
    /// Gets the type of the parameter.
    /// </summary>
    public ParameterType ParameterType { get => this.pAttr.ParameterType; }

    /// <summary>
    /// Gets the description of the parameter.
    /// </summary>
    public string? GetDescription(string commandName) => MessageService.GetString($"command-{commandName}-description-{this.Name[0]}");

    /// <summary>
    /// Validates whether the provided value can be assigned to the specified property of the command.
    /// </summary>
    /// <param name="cmd">The command object.</param>
    /// <param name="p">The property info.</param>
    /// <param name="v">The value to validate.</param>
    /// <returns>True if the value is valid; otherwise, false.</returns>
    internal static bool IsValid(object cmd, PropertyInfo p, string v)
    {
        try
        {
            SetValue(cmd, p, v);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Sets the value of the specified property on the command object, converting the value as necessary.
    /// </summary>
    /// <param name="cmd">The command object.</param>
    /// <param name="p">The property info.</param>
    /// <param name="v">The value to set.</param>
    /// <exception cref="CommandException">Thrown when the value cannot be parsed for an enum type.</exception>
    internal static void SetValue(object cmd, PropertyInfo p, string v)
    {
        var underlyingType = Nullable.GetUnderlyingType(p.PropertyType);
        if (underlyingType != null)
        {
            if (underlyingType.IsEnum)
            {
                Enum.TryParse(underlyingType, v, true, out var parsedEnum);
                if (parsedEnum != null)
                {
                    p.SetValue(cmd, parsedEnum);
                    return;
                }
                else
                {
                    var msg = MessageService.GetString("error-param_parse", new Dictionary<string, object>
                    {
                        { "name", p.Name },
                        { "values", string.Join(", ", underlyingType.GetEnumNames()) },
                    });

                    throw new CommandException("ParseParameter", msg);
                }
            }

            var obj = Convert.ChangeType(v, underlyingType);
            p.SetValue(cmd, obj);
            return;
        }

        p.SetValue(cmd, Convert.ChangeType(v, p.PropertyType));
    }

    /// <summary>
    /// Sets the value of this parameter on the specified command.
    /// </summary>
    /// <param name="cmd">The command object.</param>
    /// <param name="v">The value to set.</param>
    internal void SetValue(CosmosCommand cmd, string v)
    {
        SetValue(cmd, this.PropertyInfo, v);
    }
}
