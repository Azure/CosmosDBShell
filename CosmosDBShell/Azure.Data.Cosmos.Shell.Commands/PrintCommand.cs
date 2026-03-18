//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("print")]
[CosmosExample("print item-123 partitionKey-value", Description = "Retrieve and display a specific item by ID and partition key")]
[CosmosExample("print user-456 userId123 --database=MyDB --container=Users", Description = "Retrieve item from specific database and container")]
internal class PrintCommand : CosmosCommand
{
    [CosmosParameter("id")]
    public string? Id { get; init; }

    [CosmosParameter("key")]
    public string? PartitionKey { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        // Get connected state
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("print");
        }

        // Resolve container using the helper
        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "print",
            token);

        return await this.PrintItemAsync(container, token);
    }

    private async Task<CommandState> PrintItemAsync(Container container, CancellationToken token)
    {
        var commandState = new CommandState();

        try
        {
            var response = await container.ReadItemStreamAsync(this.Id, new PartitionKey(this.PartitionKey), cancellationToken: token);

            if (response.IsSuccessStatusCode)
            {
                using var reader = new StreamReader(response.Content);
                var content = await reader.ReadToEndAsync();

                // Parse the content as JSON for structured output
                var jsonDocument = System.Text.Json.JsonDocument.Parse(content);
                commandState.Result = new ShellJson(jsonDocument.RootElement);
            }
            else
            {
                throw new CommandException("print", MessageService.GetArgsString("command-print-error-item_not_found", "status", response.StatusCode));
            }
        }
        catch (CosmosException ex)
        {
            throw new CommandException("print", MessageService.GetArgsString("command-print-error-reading_item", "message", ex.Message));
        }

        return commandState;
    }
}