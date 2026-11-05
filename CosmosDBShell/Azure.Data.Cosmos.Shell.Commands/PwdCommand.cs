// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[CosmosCommand("pwd")]
[CosmosExample("pwd", DescriptionKey = "command-pwd-example-1")]
[McpAnnotation(
    Description = @"
Shows the current Cosmos DB location and the local working directory of the shell process.

This is an optional convenience command for users, scripts, and MCP clients that want to inspect the current navigation state.
When a command supports explicit --db and --con arguments, prefer those arguments instead of relying on navigation state.
")]
internal class PwdCommand : CosmosCommand
{
    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var currentLocation = ShellLocation.GetCurrentLocation(shell.State);
        var currentLocationMarkup = ShellLocation.GetCurrentLocationMarkup(shell.State);
        var localDirectory = Directory.GetCurrentDirectory();

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            type = "location",
            database = shell.State switch
            {
                ContainerState containerState => containerState.DatabaseName,
                DatabaseState databaseState => databaseState.DatabaseName,
                _ => null,
            },
            container = shell.State is ContainerState current ? current.ContainerName : null,
            currentLocation,
            localDirectory,
        }));
        commandState.RenderUser = () =>
        {
            var cosmosLabel = MessageService.GetString("command-pwd-cosmos-location") + ":";
            var localLabel = MessageService.GetString("command-pwd-local-directory") + ":";
            var width = Math.Max(cosmosLabel.Length, localLabel.Length) + 1;
            AnsiConsole.MarkupLine(Theme.FormatMuted(cosmosLabel.PadRight(width)) + currentLocationMarkup);
            AnsiConsole.MarkupLine(Theme.FormatMuted(localLabel.PadRight(width)) + Theme.FormatDirectory(localDirectory));
        };
        return Task.FromResult(commandState);
    }
}