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

[CosmosCommand("rm")]
[CosmosExample("rm test-*", Description = "Delete all items where partition key matches pattern starting with 'test-'")]
[CosmosExample("rm *-temp", Description = "Delete all items where partition key matches pattern ending with '-temp'")]
[CosmosExample("rm old-item-* --key=id", Description = "Delete items where 'id' field matches pattern")]
[CosmosExample("rm test-* --database=MyDB --container=Items", Description = "Delete items from specific database and container")]
[McpAnnotation(Title = "Remove Items", Restricted = true, Destructive = true)]
internal class RmCommand : CosmosCommand, IStateVisitor<ExitCode, CommandState>
{
    private PatternMatcher? matcher;
    private ShellInterpreter? shell;

    [CosmosParameter("pattern")]
    public string? Pattern { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("key", "k")]
    public string? Key { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (commandState.Result == null)
        {
            if (string.IsNullOrEmpty(this.Pattern))
            {
                throw new CommandException("rm", MessageService.GetString("command-rm-error-no_filter"));
            }
        }

        this.shell = shell;
        this.matcher = string.IsNullOrEmpty(this.Pattern) ? null : new PatternMatcher(this.Pattern);
        await shell.State.AcceptAsync(this, commandState, token);
        return commandState;
    }

    Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitDisconnectedStateAsync(DisconnectedState state, CommandState commandState, CancellationToken token)
    {
        throw new NotConnectedException("rm");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitConnectedStateAsync(ConnectedState state, CommandState commandState, CancellationToken token)
    {
        // If both database and container are specified, allow removing items
        if (!string.IsNullOrEmpty(this.Database) && !string.IsNullOrEmpty(this.Container))
        {
            return await this.RemoveItemsFromContainerAsync(state, this.Database, this.Container, commandState, token);
        }

        throw new NotInContainerException("rm");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitDatabaseStateAsync(DatabaseState state, CommandState commandState, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        if (!string.IsNullOrEmpty(this.Container))
        {
            return await this.RemoveItemsFromContainerAsync(state, databaseName, this.Container, commandState, token);
        }

        throw new NotInContainerException("rm");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitContainerStateAsync(ContainerState state, CommandState commandState, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.RemoveItemsFromContainerAsync(state, databaseName, containerName, commandState, token);
    }

    private async Task<ExitCode> RemoveItemsFromContainerAsync(ConnectedState state, string databaseName, string containerName, CommandState commandState, CancellationToken token)
    {
        if (this.shell == null)
        {
            throw new CommandException("rm", MessageService.GetString("error-shell-not-initialized"));
        }

        // Handle both pattern matching and pipe input
        bool hasPipeInput = commandState.Result != null;
        if (this.matcher == null && !hasPipeInput)
        {
            throw new CommandException("rm", MessageService.GetString("command-rm-error-no_filter"));
        }

        // Validate database and container exist
        await ValidateContainerExistsAsync(state, databaseName, containerName, "rm", token);

        var client = state.Client;
        var container = client.GetDatabase(databaseName).GetContainer(containerName);

        // Get container properties to find the partition key paths
        var partitionKeyPaths = await CosmosResourceFacade.GetPartitionKeyPathsAsync(state, databaseName, containerName, token);
        var partitionKeyPropertyNames = GetPartitionKeyPropertyNames(partitionKeyPaths);

        // Determine which key to match against (partition key by default, or custom key if specified)
        var matchKeyPropertyNames = string.IsNullOrEmpty(this.Key) ? partitionKeyPropertyNames : [this.Key];

        var totalCount = 0;

        // Process pipe input if available
        if (hasPipeInput && commandState.Result is ShellJson jsonResult)
        {
            // Handle items from pipe - check if it's an array or object with items property
            JsonElement itemsArray;

            if (jsonResult.Value.ValueKind == JsonValueKind.Array)
            {
                itemsArray = jsonResult.Value;
            }
            else if (jsonResult.Value.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
            {
                itemsArray = itemsProp;
            }
            else
            {
                // Single item - treat as array of one
                itemsArray = jsonResult.Value;
            }

            if (itemsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsArray.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idElement) &&
                        TryGetPartitionKeyElements(item, partitionKeyPropertyNames, out var pkElements))
                    {
                        var id = idElement.GetString();

                        // Check if pattern matches (if matcher is set)
                        bool shouldDelete = this.matcher == null; // No pattern = delete all
                        if (!shouldDelete && MatchesAnyPath(item, matchKeyPropertyNames, this.matcher!))
                        {
                            shouldDelete = true;
                        }
                        else if (!shouldDelete && this.Pattern == "*")
                        {
                            // Wildcard "*" should match everything, even if match key is missing
                            shouldDelete = true;
                        }

                        if (id != null && shouldDelete)
                        {
                            try
                            {
                                await container.DeleteItemAsync<object>(id, CreatePartitionKey(pkElements), cancellationToken: token);
                                totalCount++;
                            }
                            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                // Item was already deleted, skip
                            }
                        }
                    }
                }
            }
            else
            {
                // Single item from pipe
                if (itemsArray.TryGetProperty("id", out var idElement) &&
                    TryGetPartitionKeyElements(itemsArray, partitionKeyPropertyNames, out var pkElements))
                {
                    // Check if pattern matches (if matcher is set)
                    bool shouldDelete = this.matcher == null; // No pattern = delete all
                    if (!shouldDelete && MatchesAnyPath(itemsArray, matchKeyPropertyNames, this.matcher!))
                    {
                        shouldDelete = true;
                    }
                    else if (!shouldDelete && this.Pattern == "*")
                    {
                        // Wildcard "*" should match everything
                        shouldDelete = true;
                    }

                    if (shouldDelete)
                    {
                        var id = idElement.GetString();
                        if (id != null)
                        {
                            try
                            {
                                await container.DeleteItemAsync<object>(id, CreatePartitionKey(pkElements), cancellationToken: token);
                                totalCount++;
                            }
                            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                // Item was already deleted, skip
                            }
                        }
                    }
                }
            }
        }
        else if (this.matcher != null)
        {
            string query = $"SELECT * FROM c";

            using var feedIterator = container.GetItemQueryStreamIterator(query);

            while (feedIterator.HasMoreResults)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var response = await feedIterator.ReadNextAsync(token);
                using var streamReader = new StreamReader(response.Content);
                var queryDocument = JsonDocument.Parse(await streamReader.ReadToEndAsync());

                foreach (var element in queryDocument.RootElement.GetProperty("Documents").EnumerateArray())
                {
                    // Get id and partition key first - these are required for deletion
                    if (!element.TryGetProperty("id", out var idElement))
                    {
                        continue;
                    }

                    var id = idElement.GetString();
                    if (id == null)
                    {
                        continue;
                    }

                    if (!TryGetPartitionKeyElements(element, partitionKeyPropertyNames, out var pkElements))
                    {
                        AnsiConsole.MarkupLine(
                            MessageService.GetString(
                                "command-rm-warning-missing-partition-key",
                                new Dictionary<string, object>
                                {
                                    { "id", id },
                                    { "partitionKey", string.Join(',', partitionKeyPropertyNames) },
                                }));
                        continue;
                    }

                    // Check if pattern matches
                    bool shouldDelete = MatchesAnyPath(element, matchKeyPropertyNames, this.matcher);

                    if (shouldDelete)
                    {
                        try
                        {
                            await container.DeleteItemAsync<object>(id, CreatePartitionKey(pkElements), cancellationToken: token);
                            totalCount++;
                        }
                        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            // Item was already deleted, skip
                        }
                    }
                }
            }
        }

        if (totalCount > 0)
        {
            AnsiConsole.MarkupLine(
                MessageService.GetString(
                    "command-rm-deleted_items",
                    new Dictionary<string, object> { { "count", totalCount } }));
        }
        else
        {
            AnsiConsole.MarkupLine(
                MessageService.GetString(
                    "command-rm-no-matches",
                    new Dictionary<string, object>
                    {
                        { "pattern", this.Pattern ?? "pipe input" },
                        { "key", string.Join(',', matchKeyPropertyNames) },
                    }));
        }

        return new ExitCode(0);
    }

    internal static bool TryGetPartitionKeyElements(JsonElement element, IEnumerable<string> partitionKeyPropertyNames, out List<JsonElement> partitionKeyElements)
    {
        partitionKeyElements = [];
        foreach (var partitionKeyPropertyName in partitionKeyPropertyNames)
        {
            if (!TryGetNestedProperty(element, partitionKeyPropertyName, out var partitionKeyElement))
            {
                partitionKeyElements = [];
                return false;
            }

            partitionKeyElements.Add(partitionKeyElement);
        }

        return partitionKeyElements.Count > 0;
    }
}
