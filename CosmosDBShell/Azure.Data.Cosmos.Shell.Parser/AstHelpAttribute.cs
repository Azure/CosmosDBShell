// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

[AttributeUsage(AttributeTargets.Class)]
internal class AstHelpAttribute : Attribute
{
    public AstHelpAttribute(string key)
    {
        this.Key = key;
    }

    /// <summary>
    /// Gets the command name for shell execute.
    /// </summary>
    public string Key { get; }
}
