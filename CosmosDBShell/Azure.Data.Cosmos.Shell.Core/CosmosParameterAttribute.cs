// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

[AttributeUsage(AttributeTargets.Property)]
internal class CosmosParameterAttribute : Attribute
{
    public CosmosParameterAttribute(params string[] name)
    {
        this.Name = name;
        this.IsRequired = true;
    }

    public string[] Name { get; }

    public bool IsRequired { get; set; }

    public ParameterType ParameterType { get; set; } = ParameterType.Unknown;

    /// <summary>
    /// Optional Fluent localization key resolved via <see cref="MessageService"/> when the
    /// parameter is required but missing. When unset, a generic "Missing required parameter"
    /// message is used.
    /// </summary>
    public string? RequiredErrorKey { get; set; }
}
