// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System;
using Azure.Data.Cosmos.Shell.Commands;

internal class CommandRunner
{
    public CommandRunner()
    {
        foreach (var type in typeof(CosmosCommand).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(CosmosCommand))))
        {
            if (CommandFactory.TryCreateFactory(type, out var factory))
            {
                this.Commands[factory.CommandName] = factory;

                foreach (var alias in factory.Aliases)
                {
                    this.Commands[alias] = factory;
                }
            }
        }
    }

    public Dictionary<string, CommandFactory> Commands { get; set; } = [];

    public bool IsCommand(string command)
    {
        return this.Commands.ContainsKey(command);
    }

    internal bool IsExternal(string v)
    {
        if (this.Commands.TryGetValue(v, out var factory))
        {
            return factory.IsExternal;
        }

        return false;
    }

    internal bool IsOptionPrefix(string? currentCommand, string name)
    {
        if (currentCommand == null)
        {
            return true;
        }

        if (this.Commands.TryGetValue(currentCommand, out var factory))
        {
            if ("help".StartsWith(name, StringComparison.OrdinalIgnoreCase) || name == "?")
            {
                return true;
            }

            return factory.AllOptions.Any(o => o.Name.Any(optionName => optionName.StartsWith(name, StringComparison.OrdinalIgnoreCase)));
        }

        return false;
    }
}
