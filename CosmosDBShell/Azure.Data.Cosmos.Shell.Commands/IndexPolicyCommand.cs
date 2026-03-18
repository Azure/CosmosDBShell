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
using Spectre.Console;

[CosmosCommand("indexpolicy")]
[CosmosExample("indexpolicy", Description = "Display the current container's indexing policy")]
[CosmosExample("indexpolicy --database=MyDB --container=MyContainer", Description = "Display indexing policy of a specific container")]
[CosmosExample("indexpolicy {\"indexingMode\":\"consistent\",\"includedPaths\":[{\"path\":\"/*\"}],\"excludedPaths\":[{\"path\":\"/\\\"_etag\\\"/?\"}]}", Description = "Update the indexing policy with a JSON string")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Index Policy",
    Description = @"
Reads or updates the indexing policy of a Cosmos DB container.
Without a policy argument, it reads and returns the current indexing policy as JSON.
With a policy argument (JSON string), it replaces the indexing policy of the container.

The JSON format follows the Cosmos DB indexing policy schema with properties:
- indexingMode: 'consistent' or 'none'
- automatic: true or false
- includedPaths: array of { path } objects
- excludedPaths: array of { path } objects
- compositeIndexes: array of arrays of { path, order } objects
- spatialIndexes: array of { path, types } objects
",
    ReadOnly = false)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class IndexPolicyCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    [CosmosParameter("policy", IsRequired = false)]
    public string? Policy { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token) =>
        shell.State.AcceptAsync(this, shell, token);

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("indexpolicy");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database) && !string.IsNullOrEmpty(this.Container))
        {
            return await this.ExecuteOnContainerAsync(state.Client, this.Database, this.Container, token);
        }

        throw new NotInContainerException("indexpolicy");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;

        if (!string.IsNullOrEmpty(this.Container))
        {
            return await this.ExecuteOnContainerAsync(state.Client, databaseName, this.Container, token);
        }

        throw new NotInContainerException("indexpolicy");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.ExecuteOnContainerAsync(state.Client, databaseName, containerName, token);
    }

    private static async Task<CommandState> ReadIndexPolicyAsync(Container container, CancellationToken token)
    {
        var containerResponse = await container.ReadContainerAsync(cancellationToken: token);
        var resource = containerResponse.Resource;
        if (resource == null)
        {
            throw new CommandException("indexpolicy", MessageService.GetString("error-unable_to_read_container"));
        }

        var indexingPolicy = resource.IndexingPolicy;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(indexingPolicy, Newtonsoft.Json.Formatting.Indented);

        ShellInterpreter.WriteLine(json);

        using var jsonDoc = JsonDocument.Parse(json);
        var commandState = new CommandState
        {
            IsPrinted = true,
        };
        commandState.Result = new ShellJson(jsonDoc.RootElement.Clone());
        return commandState;
    }

    private async Task<CommandState> ExecuteOnContainerAsync(CosmosClient client, string databaseName, string containerName, CancellationToken token)
    {
        await ValidateContainerExistsAsync(client, databaseName, containerName, "indexpolicy", token);

        var container = client.GetDatabase(databaseName).GetContainer(containerName);

        if (string.IsNullOrEmpty(this.Policy))
        {
            return await ReadIndexPolicyAsync(container, token);
        }

        return await this.WriteIndexPolicyAsync(container, token);
    }

    private async Task<CommandState> WriteIndexPolicyAsync(Container container, CancellationToken token)
    {
        IndexingPolicy indexingPolicy;
        try
        {
            indexingPolicy = Newtonsoft.Json.JsonConvert.DeserializeObject<IndexingPolicy>(this.Policy!)
                ?? throw new CommandException("indexpolicy", MessageService.GetString("command-indexpolicy-error_invalid_policy"));
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            throw new CommandException("indexpolicy", MessageService.GetString("command-indexpolicy-error_invalid_policy"), ex);
        }

        var containerResponse = await container.ReadContainerAsync(cancellationToken: token);
        var resource = containerResponse.Resource;
        if (resource == null)
        {
            throw new CommandException("indexpolicy", MessageService.GetString("error-unable_to_read_container"));
        }

        resource.IndexingPolicy = indexingPolicy;
        var replaceResponse = await container.ReplaceContainerAsync(resource, cancellationToken: token);

        var updatedPolicy = replaceResponse.Resource.IndexingPolicy;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(updatedPolicy, Newtonsoft.Json.Formatting.Indented);

        ShellInterpreter.WriteLine(MessageService.GetString("command-indexpolicy-updated"));
        ShellInterpreter.WriteLine(json);

        using var jsonDoc = JsonDocument.Parse(json);
        var commandState = new CommandState
        {
            IsPrinted = true,
        };
        commandState.Result = new ShellJson(jsonDoc.RootElement.Clone());
        return commandState;
    }
}
