// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

[CosmosCommand("welcome")]
[CosmosExample("welcome", Description = "Display the welcome screen")]
[McpAnnotation(Restricted = true, ReadOnly = true)]
internal sealed class WelcomeCommand : CosmosCommand
{
    public override Task<CommandState> ExecuteAsync(
        ShellInterpreter shell,
        CommandState commandState,
        string commandText,
        CancellationToken token)
    {
        shell.ShowWelcome();
        commandState.Result = new ShellText(MessageService.GetString("command-welcome-result"));
        commandState.IsPrinted = true;
        return Task.FromResult(commandState);
    }
}