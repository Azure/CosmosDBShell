// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

using Spectre.Console;

[CosmosCommand("version")]
[CosmosExample("version", Description = "Display the current Cosmos Shell version")]
internal class VersionCommand : CosmosCommand
{
    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        shell.PrintVersion(commandState);

        return Task.FromResult(commandState);
    }
}
