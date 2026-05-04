//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

[CosmosCommand("create")]
[CosmosExample("create container \"Test\" \"/pk\"", Description = "Create a new container with partition key")]
[CosmosExample("create database \"My-Database\" -scale=auto -ru=1000", Description = "Create database with autoscale throughput")]
internal class CreateCommand : CosmosCommand
{
    private static readonly char[] EOL = ['\n', '\r'];

    [CosmosParameter("item", IsRequired = true)]
    public string? Item { get; init; }

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    [CosmosParameter("partition_key", IsRequired = false)]
    public string? PartitionKey { get; init; }

    [CosmosParameter("unique_key", IsRequired = false)]
    public string? UniqueKey { get; init; }

    [CosmosOption("scale", "s")]
    public string? Scale { get; init; }

    [CosmosOption("ru")]
    public int? MaxRU { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("index_policy", "ip")]
    public string? IndexPolicy { get; init; }

    [CosmosOption("force", "upsert")]
    public bool? Force { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var item = (this.Item ?? string.Empty).ToUpper();
        if (IsItem(item))
        {
            var mkItemCmd = new MakeItemCommand()
            {
                Data = this.Name, // For items, "name" parameter contains the JSON data
                Database = this.Database,
                Container = this.Container,
                Force = this.Force,
            };
            return await mkItemCmd.ExecuteAsync(shell, commandState, commandText, token);
        }

        if (IsContainer(item))
        {
            if (this.Name == null)
            {
                throw new CommandException("create", MessageService.GetString("command-create-error-container_name_required"));
            }

            if (this.PartitionKey == null)
            {
                throw new CommandException("create", MessageService.GetString("command-create-error-partition_key_required"));
            }

            return await shell.State.AcceptAsync(
                new MakeContainerCommand()
                {
                    Name = this.Name,
                    PartitionKey = this.PartitionKey,
                    UniqueKey = this.UniqueKey,
                    Scale = this.Scale,
                    MaxRU = this.MaxRU,
                    Database = this.Database,
                    IndexPolicy = this.IndexPolicy,
                },
                shell,
                token);
        }

        if (IsDatabase(item))
        {
            if (this.Name == null)
            {
                throw new CommandException("create", MessageService.GetString("command-create-error-database_name_required"));
            }

            return await shell.State.AcceptAsync(
                new MakeDbCommand()
                {
                    Name = this.Name,
                    Scale = this.Scale,
                    MaxRU = this.MaxRU,
                },
                shell,
                token);
        }

        throw new CommandException("create", MessageService.GetString("command-create-error-invalid_item_type"));
    }

    internal static bool IsDatabase(string item)
    {
        return item == "DATABASE" || item == "DB";
    }

    internal static bool IsContainer(string item)
    {
        return item == "CONTAINER" || item == "C";
    }

    internal static bool IsItem(string item)
    {
        return item == "ITEM" || item == "I";
    }
}