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
        commandState.Result = new ShellText(MessageService.GetString("command-welcome-result"));

        // Defer the banner to RenderUser so PrintState's redirection/machine-mode
        // policy decides whether to show it, instead of writing to Console.Out
        // unconditionally and corrupting deterministic stdout (e.g. -c welcome
        // --output json, or redirected output).
        commandState.RenderUser = shell.ShowWelcome;
        return Task.FromResult(commandState);
    }
}