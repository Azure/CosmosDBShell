// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

[AttributeUsage(AttributeTargets.Property)]
internal class CosmosOptionAttribute : Attribute
{
    public CosmosOptionAttribute(params string[] name)
    {
        this.Names = name;
    }

    public string[] Names { get; }

    public object? DefaultValue { get; set; }

    public bool Hidden { get; set; }
}
