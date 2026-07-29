// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[CosmosCommand("pwd")]
[CosmosExample("pwd", Description = "Show the current shell location")]
[McpAnnotation(
    Description = @"
Shows the current shell location.

This is an optional convenience command for users, scripts, and MCP clients that want to inspect the current navigation state.
When a command supports explicit --db and --con arguments, prefer those arguments instead of relying on navigation state.
")]
internal class PwdCommand : CosmosCommand
{
    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var currentLocation = ShellLocation.GetCurrentLocation(shell.State);

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            currentLocation,
        }));
        commandState.RenderUser = () => AnsiConsole.MarkupLine(ShellLocation.GetCurrentLocationMarkup(shell.State));
        return Task.FromResult(commandState);
    }
}