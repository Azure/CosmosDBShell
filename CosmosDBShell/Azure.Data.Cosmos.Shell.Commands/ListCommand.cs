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
        this.matcher = HasClientSideFilter(this.Filter) ? new PatternMatcher(this.Filter!) : null;
        return await shell.State.AcceptAsync(this, shell, token) ?? new CommandState();
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.ListContainerItemsAsync(state, shell, databaseName, containerName, token);
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
                return await this.ListContainerItemsAsync(state, interpreter, this.Database, this.Container, token);
            }

            return await this.ListDatabaseContainersAsync(state, this.Database, token);
        }

        // Default behavior: list databases
        var list = new List<string>();
        var completionList = new List<string>();
        await foreach (var databaseName in EnumerateDatabaseNamesAsync(state, "ls", token))
        {
            var trimmed = databaseName.Trim();
            completionList.Add(trimmed);

            if (!this.IsMatch(trimmed))
            {
                continue;
            }

            list.Add(trimmed);
            AnsiConsole.MarkupLine(Theme.DatabaseNamePromt(trimmed));
        }

        CosmosCompleteCommand.SetDatabases(state.Client, completionList);

        AnsiConsole.MarkupLine(MessageService.GetString("command-ls-found_databases", new Dictionary<string, object>
        {
            { "count", list.Count },
            { "display", Theme.FormatTableValue(list.Count.ToString()) },
        }));

        if (completionList.Count == 0 && state.ArmContext is not null)
        {
            AnsiConsole.MarkupLine(Theme.FormatWarning(MessageService.GetString("command-ls-empty_databases_hint")));
        }

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
            return await this.ListContainerItemsAsync(state, interpreter, databaseName, this.Container, token);
        }

        // Default behavior: list containers in the database
        return await this.ListDatabaseContainersAsync(state, databaseName, token);
    }

    private async Task<CommandState> ListDatabaseContainersAsync(ConnectedState state, string databaseName, CancellationToken token)
    {
        // Validate database exists
        await ValidateDatabaseExistsAsync(state, databaseName, "ls", token);
        var list = new List<string>();
        var completionList = new List<string>();
        await foreach (var containerName in EnumerateContainerNamesAsync(state, databaseName, "ls", token))
        {
            var trimmed = containerName.Trim();
            completionList.Add(trimmed);

            if (!this.IsMatch(trimmed))
            {
                continue;
            }

            list.Add(trimmed);
            AnsiConsole.MarkupLine(Theme.ContainerNamePromt(trimmed));
        }

        CosmosCompleteCommand.SetContainers(state.Client, databaseName, completionList);

        AnsiConsole.MarkupLine(MessageService.GetString("command-ls-found_containers", new Dictionary<string, object>
        {
            { "count", list.Count },
            { "display", Theme.FormatTableValue(list.Count.ToString()) },
            { "database", Theme.DatabaseNamePromt(databaseName) },
        }));

        if (completionList.Count == 0 && state.ArmContext is not null)
        {
            AnsiConsole.MarkupLine(Theme.FormatWarning(MessageService.GetString("command-ls-empty_containers_hint", new Dictionary<string, object>
            {
                { "database", databaseName },
            })));
        }

        var result = new CommandState
        {
            IsPrinted = true,
        };
        result.Result = new ShellJson(JsonSerializer.SerializeToElement(list));
        return result;
    }

    private async Task<CommandState> ListContainerItemsAsync(ConnectedState state, ShellInterpreter interpreter, string databaseName, string containerName, CancellationToken token)
    {
        // Validate database and container exist
        await ValidateContainerExistsAsync(state, databaseName, containerName, "ls", token);

        var client = state.Client;
        var container = client.GetDatabase(databaseName).GetContainer(containerName);
        var opt = new QueryRequestOptions();
        var effectiveMaxItemCount = ResultLimit.ResolveMaxItemCount(this.Max);
        if (effectiveMaxItemCount.HasValue)
        {
            opt.MaxItemCount = effectiveMaxItemCount.Value;
        }

        var partitionKeyPaths = await CosmosResourceFacade.GetPartitionKeyPathsAsync(state, databaseName, containerName, token);
        var partitionKeyPropertyNames = GetPartitionKeyPropertyNames(partitionKeyPaths);
        var matchKeyPropertyNames = string.IsNullOrEmpty(this.Key) ? partitionKeyPropertyNames : [this.Key];

        var queryText = BuildItemQueryText(effectiveMaxItemCount, this.Filter);
        var usesServerSideTop = effectiveMaxItemCount.HasValue && !HasClientSideFilter(this.Filter);
        using var feedIterator = container.GetItemQueryStreamIterator(queryText, requestOptions: opt);
        var returnState = new CommandState();
        returnState.SetFormat(this.OutputFormat ?? Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT"));
        var list = new List<JsonElement>();
        var limitReached = false;
        while (feedIterator.HasMoreResults)
        {
            using var response = await feedIterator.ReadNextAsync(token);
            using var queryDocument = await ReadQueryResponseAsync(response, token);

            foreach (var element in queryDocument.RootElement.GetProperty("Documents").EnumerateArray())
            {
                // Check if pattern matches
                bool shouldList = this.matcher == null;

                shouldList = shouldList || MatchesAnyPath(element, matchKeyPropertyNames, this.matcher!);

                if (shouldList)
                {
                    list.Add(element.Clone());
                }

                if (ResultLimit.IsLimitReached(list.Count, effectiveMaxItemCount))
                {
                    limitReached = ShouldReportLimitReached(list.Count, effectiveMaxItemCount, usesServerSideTop, feedIterator.HasMoreResults);
                    break;
                }
            }

            if (limitReached)
            {
                break;
            }
        }

        returnState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { items = list }));

        // Print the items before the summary so the count lands at the end, matching how `ls`
        // reports databases and containers. PrintState clears Result after printing, so preserve
        // it for downstream piping and mark the state as already printed.
        var rendered = returnState.Result;
        interpreter.PrintState(returnState);
        returnState.Result = rendered;
        returnState.IsPrinted = true;

        AnsiConsole.MarkupLine(MessageService.GetString("command-ls-found_items", new Dictionary<string, object>
        {
            { "count", list.Count },
            { "display", Theme.FormatTableValue(list.Count.ToString()) },
            { "container", Theme.ContainerNamePromt(container.Id) },
        }));
        if (limitReached && effectiveMaxItemCount.HasValue)
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-results-limit_reached", new Dictionary<string, object> { { "count", effectiveMaxItemCount.Value } }));
        }

        return returnState;
    }

    internal static async Task<JsonDocument> ReadQueryResponseAsync(ResponseMessage response, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = response.Content != null
                ? await ReadResponseContentAsync(response.Content, token)
                : string.Empty;
            var message = string.IsNullOrWhiteSpace(response.ErrorMessage) ? errorContent : response.ErrorMessage;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = MessageService.GetString("command-ls-error-request_failed", new Dictionary<string, object>
                {
                    { "statusCode", (int)response.StatusCode },
                    { "status", response.StatusCode },
                });
            }

            throw new CommandException("ls", message);
        }

        if (response.Content == null)
        {
            throw new CommandException("ls", MessageService.GetString("command-ls-error-no_content_stream"));
        }

        var responseContent = await ReadResponseContentAsync(response.Content, token);
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            throw new CommandException("ls", MessageService.GetString("command-ls-error-empty_content"));
        }

        return JsonDocument.Parse(responseContent);
    }

    private static async Task<string> ReadResponseContentAsync(Stream content, CancellationToken token)
    {
        using var streamReader = new StreamReader(content);
        return await streamReader.ReadToEndAsync(token);
    }

    private bool IsMatch(string item)
    {
        return this.matcher == null || this.matcher.Match(item);
    }

    /// <summary>
    /// Builds the SQL text for listing items in a container. When the caller
    /// supplies a finite limit and there is no client-side filter that could
    /// discard rows, switches to <c>SELECT TOP n * FROM c</c> so the server
    /// stops scanning once the requested number of rows has been produced.
    /// With a filter the server cannot honor the cap (the substring match is
    /// applied in the shell against the partition or custom key), so we fall
    /// back to <c>SELECT * FROM c</c> and rely on the existing client-side
    /// break to stop paging.
    /// </summary>
    internal static string BuildItemQueryText(int? effectiveMaxItemCount, string? filter)
    {
        if (effectiveMaxItemCount.HasValue && !HasClientSideFilter(filter))
        {
            return $"SELECT TOP {effectiveMaxItemCount.Value} * FROM c";
        }

        return "SELECT * FROM c";
    }

    internal static bool HasClientSideFilter(string? filter)
    {
        return !string.IsNullOrEmpty(filter) && filter != "*";
    }

    internal static bool ShouldReportLimitReached(int currentCount, int? effectiveMaxItemCount, bool usesServerSideTop, bool iteratorHasMoreResults)
    {
        return ResultLimit.IsLimitReached(currentCount, effectiveMaxItemCount) && (usesServerSideTop || iteratorHasMoreResults);
    }
}
