//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[CosmosCommand("disconnect")]
[CosmosExample("disconnect", Description = "Disconnect from the current Cosmos DB account")]
internal class DisconnectCommand : CosmosCommand
{
    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (shell.State is ConnectedState connectedState)
        {
            // Get account info before disconnecting
            var endpoint = connectedState.Client.Endpoint.Host;
            shell.Disconnect();

            AnsiConsole.MarkupLine(MessageService.GetArgsString("command-disconnect-success", "endpoint", endpoint));

            commandState.IsPrinted = true;
            var jsonResult = new Dictionary<string, object?>
            {
                ["disconnected"] = true,
                ["endpoint"] = endpoint,
            };
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(jsonResult));
        }
        else
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-disconnect-not_connected"));
            commandState.IsPrinted = true;
            var jsonResult = new Dictionary<string, object?>
            {
                ["disconnected"] = false,
            };
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(jsonResult));
        }

        return Task.FromResult(commandState);
    }
}