//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("mkitem")]
[CosmosExample("mkitem '{\"id\":\"1\",\"name\":\"Product\"}'", Description = "Create a single item from JSON string")]
[CosmosExample("mkitem --force '{\"id\":\"1\",\"name\":\"Updated Product\"}'", Description = "Create or replace a single item")]
[CosmosExample("echo '{\"id\":\"2\",\"price\":99.99}' | mkitem", Description = "Create item from piped input")]
[CosmosExample("mkitem '{\"id\":\"3\",\"status\":\"active\"}' --database=MyDB --container=Items", Description = "Create item in specific database and container")]
internal class MakeItemCommand : CosmosCommand
{
    private static readonly JsonDocument SuccessDocument = JsonDocument.Parse($"{{\"result\": \"success\"}}");

    [CosmosParameter("data", IsRequired = false)]
    public string? Data { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("force", "upsert")]
    public bool? Force { get; init; }

    public static object ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseJsonElement(doc.RootElement);
    }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var evaluatedResult = commandState.Result?.ConvertShellObject(DataType.Text);
        var jsonOpt = this.Data ?? (evaluatedResult as string);
        if (string.IsNullOrEmpty(jsonOpt))
        {
            throw new CommandException("mkitem", MessageService.GetString("error-no_input_data"));
        }

        // Get connected state
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("mkitem");
        }

        // Resolve container using the helper
        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "mkitem",
            token);

        await WriteItemAsync(container, commandState, jsonOpt, this.Force == true, token);

        var returnState = new CommandState();
        returnState.Result = new ShellJson(SuccessDocument.RootElement.Clone());
        return returnState;
    }

    private static object ParseJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object>();
                foreach (var property in element.EnumerateObject())
                {
                    dict.Add(property.Name, ParseJsonElement(property.Value));
                }

                return dict;

            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ParseJsonElement(item));
                }

                return list;

            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;

            case JsonValueKind.Number:
                if (element.TryGetInt32(out int intValue))
                {
                    return intValue;
                }

                if (element.TryGetInt64(out long longValue))
                {
                    return longValue;
                }

                if (element.TryGetDouble(out double doubleValue))
                {
                    return doubleValue;
                }

                return element.GetDecimal();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return null!;

            case JsonValueKind.Undefined:
            default:
                return null!;
        }
    }

    private static async Task WriteItemAsync(Container container, CommandState commandState, string? jsonOpt, bool force, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(jsonOpt))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonOpt);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    int createdCount = 0;
                    int replacedCount = 0;
                    int failCount = 0;
                    double charge = 0.0;
                    foreach (var element in root.EnumerateArray())
                    {
                        try
                        {
                            ItemResponse<JsonElement> result = force
                                ? await container.UpsertItemAsync(element, cancellationToken: token)
                                : await container.CreateItemAsync(element, cancellationToken: token);
                            charge += result.RequestCharge;

                            if (result.StatusCode == System.Net.HttpStatusCode.Created)
                            {
                                createdCount++;
                            }
                            else if (force && result.StatusCode == System.Net.HttpStatusCode.OK)
                            {
                                replacedCount++;
                            }
                            else
                            {
                                failCount++;
                                ShellInterpreter.WriteLine(
                                    MessageService.GetArgsString(
                                        "command-mkitem-error-status-returned",
                                        "status",
                                        result.StatusCode.ToString()));
                            }
                        }
                        catch (CosmosException ce)
                        {
                            failCount++;
                            ShellInterpreter.WriteLine(
                                MessageService.GetArgsString(
                                    "command-mkitem-error-creation-failed",
                                    "status",
                                    ce.StatusCode.ToString(),
                                    "message",
                                    CommandException.GetDisplayMessage(ce)));
                        }
                    }

                    if (force)
                    {
                        if ((createdCount + replacedCount) > 0 && failCount == 0)
                        {
                            ShellInterpreter.WriteLine(
                                MessageService.GetArgsString(
                                    "command-mkitem-upserted-multiple",
                                    "created",
                                    createdCount,
                                    "replaced",
                                    replacedCount,
                                    "charge",
                                    charge.ToString("F2")));
                        }
                        else if ((createdCount + replacedCount) > 0)
                        {
                            ShellInterpreter.WriteLine(
                                MessageService.GetArgsString(
                                    "command-mkitem-upserted-partial",
                                    "created",
                                    createdCount,
                                    "replaced",
                                    replacedCount,
                                    "failed",
                                    failCount,
                                    "charge",
                                    charge.ToString("F2")));
                        }
                        else
                        {
                            ShellInterpreter.WriteLine(
                                MessageService.GetArgsString(
                                    "command-mkitem-created-all-failed",
                                    "count",
                                    failCount));
                        }
                    }
                    else if (createdCount > 0 && failCount == 0)
                    {
                        ShellInterpreter.WriteLine(
                            MessageService.GetArgsString(
                                "command-mkitem-created-multiple",
                                "count",
                                createdCount,
                                "charge",
                                charge.ToString("F2")));
                    }
                    else if (createdCount > 0 && failCount > 0)
                    {
                        ShellInterpreter.WriteLine(
                            MessageService.GetArgsString(
                                "command-mkitem-created-partial",
                                "success",
                                createdCount,
                                "failed",
                                failCount,
                                "charge",
                                charge.ToString("F2")));
                    }
                    else
                    {
                        ShellInterpreter.WriteLine(
                            MessageService.GetArgsString(
                                "command-mkitem-created-all-failed",
                                "count",
                                failCount));
                    }
                }
                else
                {
                    try
                    {
                        ItemResponse<JsonElement> result = force
                            ? await container.UpsertItemAsync(root, cancellationToken: token)
                            : await container.CreateItemAsync(root, cancellationToken: token);

                        if (result.StatusCode == System.Net.HttpStatusCode.Created)
                        {
                            var key = force ? "command-mkitem-upserted-created" : "command-mkitem-created-success";
                            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                                key,
                                "charge",
                                result.RequestCharge.ToString("F2")));
                        }
                        else if (force && result.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                                "command-mkitem-upserted-replaced",
                                "charge",
                                result.RequestCharge.ToString("F2")));
                        }
                        else
                        {
                            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                                "command-mkitem-error-status-returned",
                                "status",
                                result.StatusCode.ToString()));
                        }
                    }
                    catch (CosmosException ce)
                    {
                        throw new CommandException(
                            "mkitem",
                            MessageService.GetArgsString(
                                "command-mkitem-error-creation-failed",
                                "status",
                                ce.StatusCode.ToString(),
                                "message",
                                CommandException.GetDisplayMessage(ce)),
                            ce);
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new CommandException("mkitem", MessageService.GetArgsString("json_error_parsing_arg", "message", ex.Message), ex);
            }
        }
    }
}