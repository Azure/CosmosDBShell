//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using global::Azure.Data.Cosmos.Shell.Core;
using Spectre.Console;

[CosmosCommand("cls", Aliases = ["clear"])]
[CosmosExample("cls", Description = "Clear the console screen")]
[CosmosExample("clear", Description = "Clear the console screen")]
internal class ClsCommand : CosmosCommand
{
    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        try
        {
            AnsiConsole.Clear();
        }
        catch (IOException)
        {
            // No real console attached (e.g. running under a test host). Nothing to clear.
        }

        return Task.FromResult(commandState);
    }
}
