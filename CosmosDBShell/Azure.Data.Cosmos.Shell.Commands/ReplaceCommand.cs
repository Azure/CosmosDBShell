//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("replace")]
[CosmosExample("replace '{\"id\":\"1\",\"name\":\"Updated\"}'", Description = "Replace one existing item")]
[CosmosExample("echo '{\"id\":\"2\",\"name\":\"Updated\"}' | replace", Description = "Replace item from piped input")]
[CosmosExample("replace '{\"id\":\"3\",\"status\":\"active\"}' --database=MyDB --container=Items", Description = "Replace item in specific database and container")]
internal class ReplaceCommand : CosmosCommand
{
    private static readonly JsonDocument SuccessDocument = JsonDocument.Parse("{\"result\":\"success\"}");

    [CosmosParameter("data", IsRequired = false)]
    public string? Data { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("etag")]
    public string? ETag { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var evaluatedResult = commandState.Result?.ConvertShellObject(DataType.Text);
        var jsonOpt = this.Data ?? (evaluatedResult as string);
        if (string.IsNullOrEmpty(jsonOpt))
        {
            throw new CommandException("replace", MessageService.GetString("error-no_input_data"));
        }

        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("replace");
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "replace",
            token);

        var containerResponse = await container.ReadContainerAsync(cancellationToken: token);
        var partitionKeyPath = containerResponse.Resource.PartitionKeyPath.TrimStart('/');

        await ReplaceItemsAsync(container, partitionKeyPath, jsonOpt, this.ETag, token);

        return new CommandState
        {
            Result = new ShellJson(SuccessDocument.RootElement.Clone()),
        };
    }

    private static async Task ReplaceItemsAsync(Container container, string partitionKeyPath, string jsonInput, string? etag, CancellationToken token)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonInput);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                await ReplaceArrayAsync(container, partitionKeyPath, root, etag, token);
                return;
            }

            await ReplaceOneAsync(container, partitionKeyPath, root, etag, token, printSuccess: true);
        }
        catch (JsonException ex)
        {
            throw new CommandException("replace", MessageService.GetArgsString("json_error_parsing_arg", "message", ex.Message), ex);
        }
    }

    private static async Task ReplaceArrayAsync(Container container, string partitionKeyPath, JsonElement arrayRoot, string? etag, CancellationToken token)
    {
        int successCount = 0;
        int failCount = 0;
        double charge = 0.0;

        foreach (var element in arrayRoot.EnumerateArray())
        {
            try
            {
                var itemCharge = await ReplaceOneAsync(container, partitionKeyPath, element, etag, token, printSuccess: false);
                charge += itemCharge;
                successCount++;
            }
            catch (CommandException ex)
            {
                failCount++;
                ShellInterpreter.WriteLine(ex.Message);
            }
        }

        if (successCount > 0 && failCount == 0)
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-replace-success-multiple", "count", successCount, "charge", charge.ToString("F2")));
        }
        else if (successCount > 0)
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-replace-success-partial", "success", successCount, "failed", failCount, "charge", charge.ToString("F2")));
        }
        else
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-replace-all-failed", "count", failCount));
        }
    }

    private static async Task<double> ReplaceOneAsync(Container container, string partitionKeyPath, JsonElement item, string? etag, CancellationToken token, bool printSuccess)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new CommandException("replace", MessageService.GetString("command-replace-error-invalid_item"));
        }

        if (!item.TryGetProperty("id", out var idElement) || string.IsNullOrEmpty(idElement.GetString()))
        {
            throw new CommandException("replace", MessageService.GetString("command-replace-error-missing_id"));
        }

        if (!TryGetNestedProperty(item, partitionKeyPath, out var pkElement))
        {
            throw new CommandException(
                "replace",
                MessageService.GetString(
                    "command-replace-error-missing_partition_key",
                    new Dictionary<string, object> { { "path", partitionKeyPath } }));
        }

        var requestOptions = string.IsNullOrEmpty(etag)
            ? null
            : new ItemRequestOptions { IfMatchEtag = etag };

        try
        {
            var response = await container.ReplaceItemAsync(item, idElement.GetString(), CreatePartitionKey(pkElement), requestOptions, token);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CommandException("replace", MessageService.GetArgsString("command-replace-error-status-returned", "status", response.StatusCode.ToString()));
            }

            if (printSuccess)
            {
                ShellInterpreter.WriteLine(MessageService.GetArgsString("command-replace-success-single", "charge", response.RequestCharge.ToString("F2")));
            }

            return response.RequestCharge;
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new CommandException(
                "replace",
                MessageService.GetString("command-replace-error-not_found", new Dictionary<string, object> { { "id", idElement.GetString() ?? string.Empty } }),
                ce);
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            throw new CommandException(
                "replace",
                MessageService.GetString("command-replace-error-etag_mismatch", new Dictionary<string, object> { { "id", idElement.GetString() ?? string.Empty } }),
                ce);
        }
        catch (CosmosException ce)
        {
            throw new CommandException(
                "replace",
                MessageService.GetArgsString(
                    "command-replace-error-replace-failed",
                    "status",
                    ce.StatusCode.ToString(),
                    "message",
                    CommandException.GetDisplayMessage(ce)),
                ce);
        }
    }
}
