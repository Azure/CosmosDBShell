//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Reflection;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;

internal class Option(PropertyInfo p, CosmosOptionAttribute opt)
{
    private readonly PropertyInfo p = p;
    private readonly CosmosOptionAttribute opt = opt;

    public PropertyInfo PropertyInfo { get => this.p; }

    public string[] Name => this.opt.Names;

    public bool IsBool
    {
        get
        {
            var t = Nullable.GetUnderlyingType(this.p.PropertyType) ?? this.PropertyInfo.PropertyType;
            return t.FullName == "System.Boolean";
        }
    }

    public object? DefaultValue => this.opt.DefaultValue;

    /// <summary>
    /// Gets the description of the option.
    /// </summary>
    public string? GetDescription(string commandName)
    {
        if (this.Name.Length == 0)
        {
            return null;
        }

        return MessageService.GetString($"command-{commandName}-description-{this.Name[0]}");
    }

    internal void SetValue(CosmosCommand cmd, string v)
    {
        Parameter.SetValue(cmd, this.p, v);
    }

    internal bool IsValid(string v)
    {
        if (this.IsBool)
        {
            return bool.TryParse(v, out _);
        }

        try
        {
            var underlyingType = Nullable.GetUnderlyingType(this.p.PropertyType);
            var type = underlyingType ?? this.p.PropertyType;

            if (type.IsEnum)
            {
                Enum.TryParse(type, v, true, out var parsedEnum);
                return parsedEnum != null;
            }

            Convert.ChangeType(v, type);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal bool MatchesArgument(string? arg)
    {
        return this.Name.Any(name => name.Equals(arg, StringComparison.OrdinalIgnoreCase));
    }
}