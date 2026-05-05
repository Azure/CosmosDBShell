//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("patch")]
[CosmosExample("patch set order-42 customer-7 /status active", Description = "Set a field to a string value")]
[CosmosExample("patch set order-42 customer-7 /count 42", Description = "Set a field to a number")]
[CosmosExample("patch incr order-42 customer-7 /count 1", Description = "Increment a numeric field")]
[CosmosExample("patch remove order-42 customer-7 /oldField", Description = "Remove a field")]
[CosmosExample("patch add order-42 customer-7 /tags/0 urgent", Description = "Insert at the start of an array")]
[CosmosExample("patch set order-42 customer-7 /name \"Ada Lovelace\" --etag=\"etag-value\"", Description = "Patch with optimistic concurrency")]
internal class PatchCommand : CosmosCommand
{
    private static readonly JsonDocument SuccessDocument = JsonDocument.Parse("{\"result\":\"success\"}");

    [CosmosParameter("op", RequiredErrorKey = "command-patch-error-missing_op")]
    public string? Op { get; init; }

    [CosmosParameter("id", RequiredErrorKey = "command-patch-error-missing_id")]
    public string? Id { get; init; }

    [CosmosParameter("pk", RequiredErrorKey = "command-patch-error-missing_pk")]
    public string? Key { get; init; }

    [CosmosParameter("path", RequiredErrorKey = "command-patch-error-missing_path")]
    public string? Path { get; init; }

    [CosmosParameter("value", IsRequired = false)]
    public string? Value { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("etag")]
    public string? ETag { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("patch");
        }

        if (string.IsNullOrWhiteSpace(this.Id))
        {
            throw new CommandException("patch", MessageService.GetString("command-patch-error-missing_id"));
        }

        if (string.IsNullOrWhiteSpace(this.Key))
        {
            throw new CommandException("patch", MessageService.GetString("command-patch-error-missing_pk"));
        }

        if (string.IsNullOrWhiteSpace(this.Op))
        {
            throw new CommandException("patch", MessageService.GetString("command-patch-error-missing_op"));
        }

        var op = NormalizeOperation(this.Op);
        if (!IsSupportedOperation(op))
        {
            throw new CommandException(
                "patch",
                MessageService.GetString(
                    "command-patch-error-unsupported_op",
                    new Dictionary<string, object> { { "op", op } }));
        }

        if (string.IsNullOrWhiteSpace(this.Path))
        {
            throw new CommandException("patch", MessageService.GetString("command-patch-error-missing_path"));
        }

        if (!this.Path.StartsWith("/", StringComparison.Ordinal))
        {
            throw new CommandException("patch", MessageService.GetString("command-patch-error-invalid_path"));
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "patch",
            token);

        var operation = BuildPatchOperation(op, this.Path!, this.Value);

        var requestOptions = string.IsNullOrEmpty(this.ETag)
            ? null
            : new PatchItemRequestOptions { IfMatchEtag = this.ETag };

        try
        {
            PartitionKey partitionKey;
            try
            {
                partitionKey = CreatePartitionKeyFromArgument(this.Key);
            }
            catch (JsonException ex)
            {
                throw new CommandException("patch", MessageService.GetString("command-patch-error-invalid_pk_json"), ex);
            }

            var response = await container.PatchItemAsync<JsonElement>(
                this.Id,
                partitionKey,
                new[] { operation },
                requestOptions,
                token);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CommandException("patch", MessageService.GetArgsString("command-patch-error-status-returned", "status", response.StatusCode.ToString()));
            }

            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-patch-success", "charge", response.RequestCharge.ToString("F2")));
            return new CommandState
            {
                Result = new ShellJson(SuccessDocument.RootElement.Clone()),
            };
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new CommandException(
                "patch",
                MessageService.GetString("command-patch-error-not_found", new Dictionary<string, object> { { "id", this.Id ?? string.Empty } }),
                ce);
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            throw new CommandException(
                "patch",
                MessageService.GetString("command-patch-error-etag_mismatch", new Dictionary<string, object> { { "id", this.Id ?? string.Empty } }),
                ce);
        }
        catch (CosmosException ce)
        {
            throw new CommandException(
                "patch",
                MessageService.GetArgsString(
                    "command-patch-error-failed",
                    "status",
                    ce.StatusCode.ToString(),
                    "message",
                    CommandException.GetDisplayMessage(ce)),
                ce);
        }
    }

    private static PatchOperation BuildPatchOperation(string opRaw, string path, string? value)
    {
        var op = NormalizeOperation(opRaw);

        switch (op)
        {
            case "remove":
                if (value is not null)
                {
                    throw new CommandException(
                        "patch",
                        MessageService.GetString("command-patch-error-unexpected_value_for_remove"));
                }

                return PatchOperation.Remove(path);

            case "add":
                return PatchOperation.Add(path, ParseValue(op, value));

            case "set":
                return PatchOperation.Set(path, ParseValue(op, value));

            case "replace":
                return PatchOperation.Replace(path, ParseValue(op, value));

            case "incr":
            case "increment":
                return BuildIncrementOperation(path, value);

            default:
                throw new CommandException(
                    "patch",
                    MessageService.GetString(
                        "command-patch-error-unsupported_op",
                        new Dictionary<string, object> { { "op", op } }));
        }
    }

    private static string NormalizeOperation(string op) => op.Trim().ToLowerInvariant();

    private static bool IsSupportedOperation(string op)
    {
        return op is "set" or "add" or "replace" or "remove" or "incr" or "increment";
    }

    private static PatchOperation BuildIncrementOperation(string path, string? rawValue)
    {
        if (rawValue == null)
        {
            throw new CommandException(
                "patch",
                MessageService.GetString(
                    "command-patch-error-missing_value_for_op",
                    new Dictionary<string, object> { { "op", "incr" } }));
        }

        var trimmed = rawValue.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return PatchOperation.Increment(path, intValue);
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return PatchOperation.Increment(path, doubleValue);
        }

        throw new CommandException(
            "patch",
            MessageService.GetString("command-patch-error-increment_number"));
    }

    private static object? ParseValue(string op, string? rawValue)
    {
        if (rawValue == null)
        {
            throw new CommandException(
                "patch",
                MessageService.GetString(
                    "command-patch-error-missing_value_for_op",
                    new Dictionary<string, object> { { "op", op } }));
        }

        var trimmed = rawValue.Trim();
        if (LooksLikeJsonLiteral(trimmed))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                return JsonSerializer.Deserialize<object?>(doc.RootElement.GetRawText());
            }
            catch (JsonException)
            {
                // Fall through and treat as plain string.
            }
        }

        return rawValue;
    }

    private static bool LooksLikeJsonLiteral(string trimmed)
    {
        if (trimmed.Length == 0)
        {
            return false;
        }

        var first = trimmed[0];
        if (first == '{' || first == '[' || first == '"')
        {
            return true;
        }

        if (first == '-' || char.IsDigit(first))
        {
            return true;
        }

        if (string.Equals(trimmed, "true", StringComparison.Ordinal)
            || string.Equals(trimmed, "false", StringComparison.Ordinal)
            || string.Equals(trimmed, "null", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
