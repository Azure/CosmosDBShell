//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Collections.Generic;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("print")]
[CosmosExample("print item-123 partitionKey-value", DescriptionKey = "command-print-example-1")]
[CosmosExample("print user-456 userId123 --database=MyDB --container=Users", DescriptionKey = "command-print-example-2")]
[CosmosExample("print product-680 --pk=680", DescriptionKey = "command-print-example-3")]
[CosmosExample("print product-680 --pk='\"680\"'", DescriptionKey = "command-print-example-4")]
[CosmosExample("print customer-1001 --pk='[1001,\"customer-1001\"]'", DescriptionKey = "command-print-example-5")]
internal class PrintCommand : CosmosCommand
{
    [CosmosParameter("id", RequiredErrorKey = "command-print-error-missing_id")]
    public string? Id { get; init; }

    [CosmosParameter("key", IsRequired = false)]
    public string? PartitionKey { get; init; }

    [CosmosOption("partition-key", "pk")]
    public string? PartitionKeyArgument { get; init; }

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

        if (string.IsNullOrEmpty(this.Id))
        {
            throw new CommandException("print", MessageService.GetString("command-print-error-missing_id"));
        }

        var partitionKey = this.GetPartitionKey();

        // Resolve container using the helper
        var (databaseName, containerName, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "print",
            token);

        var paths = await CosmosResourceFacade.GetPartitionKeyPathsAsync(connectedState, databaseName!, containerName!, token);
        ValidatePartitionKeyComponentCount(partitionKey, paths.Count);

        return await this.PrintItemAsync(container, partitionKey, token);
    }

    internal static void ValidatePartitionKeyComponentCount(PartitionKey partitionKey, int expected)
    {
        var actual = GetPartitionKeyComponentCount(partitionKey);
        if (actual != expected)
        {
            throw new CommandException("print", MessageService.GetString(
                "command-print-error-partition_key_components",
                new Dictionary<string, object> { { "expected", expected }, { "actual", actual } }));
        }
    }

    internal PartitionKey GetPartitionKey()
    {
        if (this.PartitionKey != null && this.PartitionKeyArgument != null)
        {
            throw new CommandException("print", MessageService.GetString("command-print-error-conflicting_keys"));
        }

        if (this.PartitionKeyArgument != null)
        {
            try
            {
                return CreatePartitionKeyFromArgument(this.PartitionKeyArgument);
            }
            catch (JsonException ex)
            {
                throw new CommandException("print", MessageService.GetString("command-print-error-invalid_pk_json"), ex);
            }
        }

        if (this.PartitionKey == null)
        {
            throw new CommandException("print", MessageService.GetString("command-print-error-missing_key"));
        }

        return new PartitionKey(this.PartitionKey);
    }

    private async Task<CommandState> PrintItemAsync(Container container, PartitionKey partitionKey, CancellationToken token)
    {
        var commandState = new CommandState();

        try
        {
            using var response = await container.ReadItemStreamAsync(this.Id, partitionKey, cancellationToken: token);
            RequestChargeContext.Record(response.Headers.RequestCharge);

            if (response.IsSuccessStatusCode)
            {
                using var reader = new StreamReader(response.Content);
                var content = await reader.ReadToEndAsync();

                // Parse the content as JSON for structured output
                using var jsonDocument = System.Text.Json.JsonDocument.Parse(content);
                commandState.Result = new ShellJson(jsonDocument.RootElement.Clone());
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new CommandException("print", MessageService.GetString("command-print-error-item_not_found", new Dictionary<string, object>
                {
                    { "id", this.Id ?? "(null)" },
                    { "partitionKey", this.PartitionKeyArgument ?? this.PartitionKey ?? "(null)" },
                }));
            }
            else
            {
                throw new CommandException("print", MessageService.GetString("command-print-error-request_failed", new Dictionary<string, object>
                {
                    { "id", this.Id ?? "(null)" },
                    { "status", (int)response.StatusCode },
                }));
            }
        }
        catch (CosmosException ex)
        {
            RequestChargeContext.Record(ex.RequestCharge);
            throw new CommandException("print", MessageService.GetArgsString("command-print-error-reading_item", "message", CommandException.GetDisplayMessage(ex)), ex);
        }

        return commandState;
    }
}