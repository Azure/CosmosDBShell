//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;

[CosmosCommand("delete")]
[CosmosExample("delete item test-*", Description = "Delete items matching the pattern")]
[CosmosExample("delete container OldContainer", Description = "Delete a container")]
[CosmosExample("delete database TestDB", Description = "Delete a database")]
[McpAnnotation(Restricted = true, Destructive = true)]
internal class DeleteCommand : CosmosCommand
{
    [CosmosParameter("item", IsRequired = true)]
    public string? Item { get; init; }

    [CosmosParameter("pattern", IsRequired = true)]
    public string? Pattern { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(shell);
        var item = (this.Item ?? string.Empty).ToUpperInvariant();
        if (CreateCommand.IsItem(item))
        {
            await shell.State.AcceptAsync(
                new RmCommand()
                {
                    Pattern = this.Pattern,
                    Database = this.Database,
                    Container = this.Container,
                },
                commandState,
                token);
        }
        else if (CreateCommand.IsContainer(item))
        {
            await shell.State.AcceptAsync(
                new RmContainerCommand()
                {
                    Name = this.Pattern,
                    Database = this.Database,
                },
                shell,
                token);
        }
        else if (CreateCommand.IsDatabase(item))
        {
            await shell.State.AcceptAsync(
                new RmDbCommand()
                {
                    Name = this.Pattern,
                },
                shell,
                token);
        }
        else
        {
            throw new CommandException("delete", MessageService.GetString("command-delete-error-invalid_item_type"));
        }

        return commandState;
    }
}