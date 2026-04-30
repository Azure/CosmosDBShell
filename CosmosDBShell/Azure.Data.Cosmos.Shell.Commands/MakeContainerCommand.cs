//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("mkcon")]
[CosmosExample("mkcon Products /categoryId", Description = "Create container with single partition key")]
[CosmosExample("mkcon Orders /customerId,/orderId", Description = "Create container with hierarchical partition keys")]
[CosmosExample("mkcon Users /userId -unique_key=/email -scale=auto -ru=4000", Description = "Create container with unique key constraint and autoscale throughput")]
[CosmosExample("mkcon Items /pk --database=TestDB", Description = "Create container in specific database")]
[CosmosExample("mkcon Logs /id --index_policy={\"indexingMode\":\"consistent\",\"includedPaths\":[{\"path\":\"/*\"}],\"excludedPaths\":[{\"path\":\"/\\\"_etag\\\"/?\"}]}", Description = "Create container with custom indexing policy")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(Description = @"
Supports hierarchical partition keys by specifying multiple partition key paths separated by commas.
For a single partition key use: /partitionKeyPath
For hierarchical partition keys use: /path1,/path2 or /path1,/path2,/path3
Each path must start with a forward slash (/).

Supports custom indexing policy via the --index_policy option as a JSON string.
The JSON format follows the Cosmos DB indexing policy schema.
Use the 'indexpolicy' command to read or update the indexing policy of an existing container.
")]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class MakeContainerCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    [CosmosParameter("name")]
    public string? Name { get; init; }

    [CosmosParameter("partition_key", IsRequired = true)]
    public string? PartitionKey { get; init; }

    [CosmosParameter("unique_key", IsRequired = false)]
    public string? UniqueKey { get; init; }

    [CosmosOption("scale", "s")]
    public string? Scale { get; init; }

    [CosmosOption("ru")]
    public int? MaxRU { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("index_policy", "ip")]
    public string? IndexPolicy { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        return await shell.State.AcceptAsync(this, shell, token);
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("mkcon");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database))
        {
            return await this.CreateContainerInDatabaseAsync(state.Client, this.Database, token);
        }

        throw new NotInDatabaseException("mkcon");
    }

    public ContainerProperties CreateContainerProperties(Database db)
    {
        var keys = (this.PartitionKey ?? string.Empty).Split(",", StringSplitOptions.TrimEntries).ToList();
        if (string.IsNullOrEmpty(keys[0]))
        {
            throw new CommandException("mkcon", MessageService.GetString("command-mkcon-error_partition_key_empty"));
        }

        if (keys.Any(key => !key.StartsWith("/")))
        {
            throw new CommandException("mkcon", MessageService.GetString("command-mkcon-error_partition_key_slash"));
        }

        if (keys.Count > 1)
        {
            var hpkProperties = new ContainerProperties(this.Name, keys);
            if (this.UniqueKey != null)
            {
                AddUniqueKeyPolicy(hpkProperties, this.UniqueKey);
            }

            return hpkProperties;
        }

        var def = db.DefineContainer(this.Name, keys[0]);
        if (this.UniqueKey != null)
        {
            var key = def.WithUniqueKey();

            foreach (var uk in (this.UniqueKey ?? string.Empty).Split("/").Select(s => "/" + s))
            {
                key = key.Path(uk);
            }

            def = key.Attach();
        }

        var cp = def.Build();
        return cp;
    }

    private static void AddUniqueKeyPolicy(ContainerProperties properties, string uniqueKey)
    {
        var key = new UniqueKey();
        foreach (var uk in uniqueKey.Split("/").Select(s => "/" + s))
        {
            key.Paths.Add(uk);
        }

        properties.UniqueKeyPolicy.UniqueKeys.Add(key);
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database) && !string.Equals(this.Database, state.DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            return await this.CreateContainerInDatabaseAsync(state.Client, this.Database, token);
        }

        return await this.CreateContainerInDatabaseAsync(state.Client, state.DatabaseName, token);
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database))
        {
            return ((IStateVisitor<CommandState, ShellInterpreter>)this).VisitDatabaseStateAsync(state, shell, token);
        }

        throw new CommandException("mkcon", MessageService.GetString("error-not_allowed_in_container"));
    }

    private async Task<CommandState> CreateContainerInDatabaseAsync(CosmosClient client, string databaseName, CancellationToken token)
    {
        // Validate database exists
        await ValidateDatabaseExistsAsync(client, databaseName, "mkcon", token);

        var db = client.GetDatabase(databaseName);
        var cp = this.CreateContainerProperties(db);

        if (!string.IsNullOrEmpty(this.IndexPolicy))
        {
            try
            {
                var indexingPolicy = Newtonsoft.Json.JsonConvert.DeserializeObject<IndexingPolicy>(this.IndexPolicy)
                    ?? throw new CommandException("mkcon", MessageService.GetString("command-mkcon-error_invalid_index_policy"));
                cp.IndexingPolicy = indexingPolicy;
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                throw new CommandException("mkcon", MessageService.GetString("command-mkcon-error_invalid_index_policy"), ex);
            }
        }

        var tp = MakeDbCommand.CreateThroughputProperties(this.Scale, this.MaxRU);
        var container = await db.CreateContainerIfNotExistsAsync(cp, tp, cancellationToken: token);
        CosmosCompleteCommand.ClearContainers();
        ShellInterpreter.WriteLine(MessageService.GetString("command-mkcon-CreatedContainer", new Dictionary<string, object> { { "container", container.Container.Id } }));
        var commandState = new CommandState
        {
            IsPrinted = true,
        };
        var jsonObject = new { created_container = container.Container.Id };
        var jsonString = JsonSerializer.Serialize(jsonObject);
        using var jsonDoc = JsonDocument.Parse(jsonString);
        commandState.Result = new ShellJson(jsonDoc.RootElement.Clone());
        return commandState;
    }
}