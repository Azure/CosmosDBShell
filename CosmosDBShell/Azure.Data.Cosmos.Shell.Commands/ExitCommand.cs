//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Mcp;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("exit")]
[CosmosExample("exit", Description = "Exit the Cosmos DB Shell")]
[McpAnnotation(Title = "Exit Shell", Restricted = true)]
internal class ExitCommand : CosmosCommand
{
    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        shell.IsRunning = false;
        return Task.FromResult(commandState);
    }
}
