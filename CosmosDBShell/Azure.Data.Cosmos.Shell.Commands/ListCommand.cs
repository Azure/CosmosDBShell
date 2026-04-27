//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using System.Xml.Linq;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

[CosmosCommand("ls")]
[CosmosExample("ls", Description = "List all databases, containers, or items depending on current context")]
[CosmosExample("ls *Test*", Description = "Filter results using wildcard pattern")]
[CosmosExample("ls -max=10", Description = "Limit results to maximum of 10 items")]
[CosmosExample("ls -max=0", Description = "List all matching items without a limit")]
[CosmosExample("ls --database=MyDB --container=Products", Description = "List items from specific database and container")]
[CosmosExample("ls \"*active*\" --format=table", Description = "Filter and display results in table format")]
[CosmosExample("ls active --key=status", Description = "Filter items where 'status' field equals 'active'")]
internal class ListCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    private PatternMatcher? matcher;

    [CosmosParameter("filter", IsRequired = false)]
    public string? Filter { get; init; }

    [CosmosOption("max", "m")]
    public int? Max { get; init; }

    [CosmosOption("format", "f")]
    public string? OutputFormat { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("key", "k")]
    public string? Key { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        this.matcher = string.IsNullOrEmpty(this.Filter) ? null : new PatternMatcher(this.Filter);
        return await shell.State.AcceptAsync(this, shell, token) ?? new CommandState();
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.ListContainerItemsAsync(state.Client, databaseName, containerName, token);
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter interpreter, CancellationToken token)
    {
        throw new NotConnectedException("ls");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter interpreter, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database))
        {
            if (!string.IsNullOrEmpty(this.Container))
            {
                return await this.ListContainerItemsAsync(state.Client, this.Database, this.Container, token);
            }

            return await this.ListDatabaseContainersAsync(state.Client, this.Database, token);
        }

        // Default behavior: list databases
        var list = new List<string>();
        var completionList = new List<string>();
        await foreach (var database in EnumerateDatabasesAsync(state.Client))
        {
            var databaseName = database.Id.Trim();
            completionList.Add(databaseName);

            if (!this.IsMatch(database.Id))
            {
                continue;
            }

            var cn = Markup.Escape(databaseName);
            list.Add(cn);
            AnsiConsole.MarkupLine($"[green]{cn}[/]");
        }

        CosmosCompleteCommand.SetDatabases(state.Client, completionList);

        var result = new CommandState
        {
            IsPrinted = true,
        };
        result.Result = new ShellJson(JsonSerializer.SerializeToElement(list));
        return result;
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter interpreter, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;

        // If container is specified, list items in that container
        if (!string.IsNullOrEmpty(this.Container))
        {
            return await this.ListContainerItemsAsync(state.Client, databaseName, this.Container, token);
        }

        // Default behavior: list containers in the database
        return await this.ListDatabaseContainersAsync(state.Client, databaseName, token);
    }

    private async Task<CommandState> ListDatabaseContainersAsync(CosmosClient client, string databaseName, CancellationToken token)
    {
        // Validate database exists
        await ValidateDatabaseExistsAsync(client, databaseName, "ls", token);
        var db = client.GetDatabase(databaseName);
        var list = new List<string>();
        var completionList = new List<string>();
        await foreach (var container in EnumerateContainersAsync(db))
        {
            var containerName = container.Id.Trim();
            completionList.Add(containerName);

            if (!this.IsMatch(container.Id))
            {
                continue;
            }

            var cn = Markup.Escape(containerName);
            list.Add(cn);
            AnsiConsole.MarkupLine($"[magenta]{cn}[/]");
        }

        CosmosCompleteCommand.SetContainers(client, databaseName, completionList);

        var result = new CommandState
        {
            IsPrinted = true,
        };
        result.Result = new ShellJson(JsonSerializer.SerializeToElement(list));
        return result;
    }

    private async Task<CommandState> ListContainerItemsAsync(CosmosClient client, string databaseName, string containerName, CancellationToken token)
    {
        // Validate database and container exist
        await ValidateContainerExistsAsync(client, databaseName, containerName, "ls", token);

        var container = client.GetDatabase(databaseName).GetContainer(containerName);
        AnsiConsole.MarkupLine(MessageService.GetString("command-ls-container", new Dictionary<string, object> { { "container", Theme.ContainerNamePromt(container.Id) } }));
        var opt = new QueryRequestOptions();
        var effectiveMaxItemCount = ResultLimit.ResolveMaxItemCount(this.Max);
        if (effectiveMaxItemCount.HasValue)
        {
            opt.MaxItemCount = effectiveMaxItemCount.Value;
        }

        var containerResponse = await container.ReadContainerAsync(cancellationToken: token);
        var partitionKeyPath = containerResponse.Resource.PartitionKeyPath;

        // Remove the leading '/' to get the property name
        var partitionKeyPropertyName = partitionKeyPath.TrimStart('/');

        // Determine which key to match against (partition key by default, or custom key if specified)
        var matchKeyPropertyName = string.IsNullOrEmpty(this.Key) ? partitionKeyPropertyName : this.Key;

        using var feedIterator = container.GetItemQueryStreamIterator("SELECT * FROM c", requestOptions: opt);
        var returnState = new CommandState();
        returnState.SetFormat(this.OutputFormat ?? Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT"));
        var list = new List<JsonElement>();
        var limitReached = false;
        while (feedIterator.HasMoreResults)
        {
            var response = await feedIterator.ReadNextAsync(token);
            using var streamReader = new StreamReader(response.Content);
            var queryDocument = JsonDocument.Parse(await streamReader.ReadToEndAsync());

            foreach (var element in queryDocument.RootElement.GetProperty("Documents").EnumerateArray())
            {
                // Check if pattern matches
                bool shouldList = this.matcher == null || this.Filter == "*"; // No filter or wildcard = list all

                if (!shouldList && TryGetNestedProperty(element, matchKeyPropertyName, out var matchKeyElement))
                {
                    var matchKeyValue = GetValueAsString(matchKeyElement);
                    shouldList = this.matcher!.Match(matchKeyValue);
                }

                if (shouldList)
                {
                    list.Add(element);
                }

                if (ResultLimit.IsLimitReached(list.Count, effectiveMaxItemCount))
                {
                    limitReached = feedIterator.HasMoreResults;
                    break;
                }
            }

            if (limitReached)
            {
                break;
            }
        }

        returnState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { items = list }));
        AnsiConsole.MarkupLine(MessageService.GetString("command-ls-found_items", new Dictionary<string, object> { { "count", "[white]" + list.Count + "[/]" } }));
        if (limitReached && effectiveMaxItemCount.HasValue)
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-results-limit_reached", new Dictionary<string, object> { { "count", effectiveMaxItemCount.Value } }));
        }

        return returnState;
    }

    private bool IsMatch(string item)
    {
        return this.matcher == null || this.matcher.Match(item);
    }
}
