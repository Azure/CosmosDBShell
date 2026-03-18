// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Identity;
using Microsoft.Azure.Cosmos;
using RadLine;
using Spectre.Console;

internal class VariableContainer
{
    public Dictionary<string, ShellObject> Variables { get; } = new();

    internal void Declare(string v, ShellText shellText)
    {
        throw new NotImplementedException();
    }

    internal void Set(string name, ShellObject value)
    {
        this.Variables[name] = value;
    }

    internal bool TryGetValue(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ShellObject? value)
    {
        return this.Variables.TryGetValue(name, out value);
    }
}
