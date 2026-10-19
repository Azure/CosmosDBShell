//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("replace")]
[CosmosExample("replace '{\"id\":\"1\",\"name\":\"Updated\"}'", DescriptionKey = "command-replace-example-1")]
[CosmosExample("echo '{\"id\":\"2\",\"name\":\"Updated\"}' | replace", DescriptionKey = "command-replace-example-2")]
[CosmosExample("replace '{\"id\":\"3\",\"status\":\"active\"}' --database=MyDB --container=Items", DescriptionKey = "command-replace-example-3")]
internal class ReplaceCommand : CosmosCommand
{
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

        var (_, _, container, partitionKeyPaths) = await ResolveContainerWithPartitionKeyPathsAsync(
            connectedState,
            this.Database,
            this.Container,
            "replace",
            token);

        var summary = await ReplaceItemsAsync(shell, container, partitionKeyPaths, jsonOpt, this.ETag, commandState, token);

        var resultState = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                type = "item",
                replaced = summary.Replaced,
                failed = summary.Failed,
                requestCharge = summary.RequestCharge,
            })),
            RequestCharge = summary.RequestCharge,
        };
        return resultState;
    }

    private static async Task<ReplaceSummary> ReplaceItemsAsync(
        ShellInterpreter shell,
        Container container,
        IReadOnlyList<string> partitionKeyPaths,
        string jsonInput,
        string? etag,
        CommandState commandState,
        CancellationToken token)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonInput);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                if (!string.IsNullOrEmpty(etag))
                {
                    throw new CommandException("replace", MessageService.GetString("command-replace-error-etag_array_not_supported"));
                }

                var undefinedPartitionKeyCount =
                    root.EnumerateArray().Count(
                        item => item.ValueKind == JsonValueKind.Object
                            && HasUndefinedPartitionKeyComponent(item, partitionKeyPaths));
                AddUndefinedPartitionKeyWarning(commandState, undefinedPartitionKeyCount);
                shell.RenderMutationWarnings(commandState);
                return await ReplaceArrayAsync(shell, container, partitionKeyPaths, root, token);
            }

            var undefinedCount = root.ValueKind == JsonValueKind.Object
                && HasUndefinedPartitionKeyComponent(root, partitionKeyPaths)
                ? 1
                : 0;
            AddUndefinedPartitionKeyWarning(commandState, undefinedCount);
            shell.RenderMutationWarnings(commandState);
            var singleCharge = await ReplaceOneAsync(shell, container, partitionKeyPaths, root, etag, token, printSuccess: true);
            return new ReplaceSummary(1, 0, singleCharge);
        }
        catch (JsonException ex)
        {
            throw new CommandException("replace", MessageService.GetArgsString("json_error_parsing_arg", "message", ex.Message), ex);
        }
    }

    private static async Task<ReplaceSummary> ReplaceArrayAsync(ShellInterpreter shell, Container container, IReadOnlyList<string> partitionKeyPaths, JsonElement arrayRoot, CancellationToken token)
    {
        int successCount = 0;
        int failCount = 0;
        double charge = 0.0;
        var presentResults = !shell.IsMachineMode;
        var errors = presentResults ? null : new BulkWriteDiagnostics();
        var itemIndex = 0;

        foreach (var element in arrayRoot.EnumerateArray())
        {
            itemIndex++;
            try
            {
                var itemCharge = await ReplaceOneAsync(shell, container, partitionKeyPaths, element, etag: null, token, printSuccess: false);
                charge += itemCharge;
                successCount++;
            }
            catch (CommandException ex)
            {
                RequestChargeContext.Record(RequestChargeContext.GetCosmosExceptionCharge(ex));
                failCount++;
                errors?.Add(MessageService.GetArgsString("error-bulk-item", "index", itemIndex, "message", ex.Message));
                if (presentResults)
                {
                    shell.Output.WriteLine(ShellMessageKind.Error, ex.Message);
                }
            }
        }

        if (presentResults)
        {
            if (successCount > 0 && failCount == 0)
            {
                shell.Output.WriteLine(ShellMessageKind.Information, MessageService.GetArgsString("command-replace-success-multiple", "count", successCount, "charge", charge.ToString("F2")));
            }
            else if (successCount > 0)
            {
                shell.Output.WriteLine(ShellMessageKind.Warning, MessageService.GetArgsString("command-replace-success-partial", "success", successCount, "failed", failCount, "charge", charge.ToString("F2")));
            }
            else
            {
                shell.Output.WriteLine(ShellMessageKind.Error, MessageService.GetArgsString("command-replace-all-failed", "count", failCount));
            }
        }

        if (failCount > 0)
        {
            RequestChargeContext.Record(charge);
            var message = MessageService.GetArgsString(
                "command-replace-error-array_failed",
                "failed",
                failCount,
                "total",
                successCount + failCount);
            if (errors != null)
            {
                message += Environment.NewLine + errors.GetMessage();
            }

            throw new CommandException("replace", message);
        }

        return new ReplaceSummary(successCount, failCount, charge);
    }

    private static async Task<double> ReplaceOneAsync(ShellInterpreter shell, Container container, IReadOnlyList<string> partitionKeyPaths, JsonElement item, string? etag, CancellationToken token, bool printSuccess)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new CommandException("replace", MessageService.GetString("command-replace-error-invalid_item"));
        }

        if (!item.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(idElement.GetString()))
        {
            throw new CommandException("replace", MessageService.GetString("command-replace-error-missing_id"));
        }

        var id = idElement.GetString();
        PartitionKey partitionKey;
        try
        {
            if (!TryCreatePartitionKeyFromItem(item, partitionKeyPaths, out _, out partitionKey))
            {
                throw new CommandException(
                    "replace",
                    MessageService.GetString(
                        "command-replace-error-missing_partition_key",
                        new Dictionary<string, object> { { "path", string.Join(", ", partitionKeyPaths) } }));
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new CommandException(
                "replace",
                MessageService.GetArgsString(
                    "command-replace-error-replace-failed",
                    "status",
                    "Invalid partition key",
                    "message",
                    ex.Message),
                ex);
        }

        var requestOptions = string.IsNullOrEmpty(etag)
            ? null
            : new ItemRequestOptions { IfMatchEtag = etag };

        try
        {
            using var body = new MemoryStream(Encoding.UTF8.GetBytes(item.GetRawText()));
            using var response = await container.ReplaceItemStreamAsync(body, id, partitionKey, requestOptions, token);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CommandException("replace", MessageService.GetArgsString("command-replace-error-status-returned", "status", response.StatusCode.ToString()));
            }

            if (printSuccess)
            {
                shell.Output.WriteLine(ShellMessageKind.Information, MessageService.GetArgsString("command-replace-success-single", "charge", response.Headers.RequestCharge.ToString("F2")));
            }

            return response.Headers.RequestCharge;
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new CommandException(
                "replace",
                MessageService.GetString("command-replace-error-not_found", new Dictionary<string, object> { { "id", id ?? string.Empty } }),
                ce);
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            throw new CommandException(
                "replace",
                MessageService.GetString("command-replace-error-etag_mismatch", new Dictionary<string, object> { { "id", id ?? string.Empty } }),
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

    private readonly record struct ReplaceSummary(int Replaced, int Failed, double RequestCharge);
}
