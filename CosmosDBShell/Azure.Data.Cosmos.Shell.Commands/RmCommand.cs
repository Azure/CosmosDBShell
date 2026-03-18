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

    public static new IAsyncEnumerable<DatabaseProperties> EnumerateDatabasesAsync(CosmosClient client)
    {
        throw new NotInContainerException("rm");
    }

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
            return await this.RemoveItemsFromContainerAsync(state.Client, this.Database, this.Container, commandState, token);
        }

        throw new NotInContainerException("rm");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitDatabaseStateAsync(DatabaseState state, CommandState commandState, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        if (!string.IsNullOrEmpty(this.Container))
        {
            return await this.RemoveItemsFromContainerAsync(state.Client, databaseName, this.Container, commandState, token);
        }

        throw new NotInContainerException("rm");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitContainerStateAsync(ContainerState state, CommandState commandState, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.RemoveItemsFromContainerAsync(state.Client, databaseName, containerName, commandState, token);
    }

    private async Task<ExitCode> RemoveItemsFromContainerAsync(CosmosClient client, string databaseName, string containerName, CommandState commandState, CancellationToken token)
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
        await ValidateContainerExistsAsync(client, databaseName, containerName, "rm", token);

        var container = client.GetDatabase(databaseName).GetContainer(containerName);

        // Get container properties to find the partition key path
        var containerResponse = await container.ReadContainerAsync(cancellationToken: token);
        var partitionKeyPath = containerResponse.Resource.PartitionKeyPath;

        // Remove the leading '/' to get the property name
        var partitionKeyPropertyName = partitionKeyPath.TrimStart('/');

        // Determine which key to match against (partition key by default, or custom key if specified)
        var matchKeyPropertyName = string.IsNullOrEmpty(this.Key) ? partitionKeyPropertyName : this.Key;

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
                        TryGetNestedProperty(item, partitionKeyPropertyName, out var pkElement))
                    {
                        var id = idElement.GetString();

                        // Check if pattern matches (if matcher is set)
                        bool shouldDelete = this.matcher == null; // No pattern = delete all
                        if (!shouldDelete && TryGetNestedProperty(item, matchKeyPropertyName, out var matchKeyElement))
                        {
                            var matchKeyValue = GetValueAsString(matchKeyElement);
                            shouldDelete = this.matcher!.Match(matchKeyValue);
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
                                await container.DeleteItemAsync<object>(id, CreatePartitionKey(pkElement), cancellationToken: token);
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
                    TryGetNestedProperty(itemsArray, partitionKeyPropertyName, out var pkElement))
                {
                    // Check if pattern matches (if matcher is set)
                    bool shouldDelete = this.matcher == null; // No pattern = delete all
                    if (!shouldDelete && TryGetNestedProperty(itemsArray, matchKeyPropertyName, out var matchKeyElement))
                    {
                        var matchKeyValue = GetValueAsString(matchKeyElement);
                        shouldDelete = this.matcher!.Match(matchKeyValue);
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
                                await container.DeleteItemAsync<object>(id, CreatePartitionKey(pkElement), cancellationToken: token);
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

                    if (!TryGetNestedProperty(element, partitionKeyPropertyName, out var pkElement))
                    {
                        AnsiConsole.MarkupLine(
                            MessageService.GetString(
                                "command-rm-warning-missing-partition-key",
                                new Dictionary<string, object>
                                {
                                    { "id", id },
                                    { "partitionKey", partitionKeyPropertyName },
                                }));
                        continue;
                    }

                    // Check if pattern matches
                    bool shouldDelete = false;
                    if (TryGetNestedProperty(element, matchKeyPropertyName, out var matchKeyElement))
                    {
                        var matchKeyValue = GetValueAsString(matchKeyElement);
                        shouldDelete = this.matcher.Match(matchKeyValue);
                    }

                    if (shouldDelete)
                    {
                        try
                        {
                            await container.DeleteItemAsync<object>(id, CreatePartitionKey(pkElement), cancellationToken: token);
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
                        { "key", matchKeyPropertyName },
                    }));
        }

        return new ExitCode(0);
    }
}
