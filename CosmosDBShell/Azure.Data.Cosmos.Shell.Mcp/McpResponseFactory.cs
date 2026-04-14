// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using ModelContextProtocol.Protocol;

internal static class McpResponseFactory
{
    private const string DefaultErrorMessage = "Command execution failed.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static CallToolResult CreateSuccess(CommandState commandState, State shellState)
    {
        return CreateResponse(CreateSuccessPayload(commandState), shellState, commandState.IsError);
    }

    public static CallToolResult CreateError(string message, State shellState)
    {
        return CreateResponse(
            new JsonObject
            {
                ["error"] = message,
            },
            shellState,
            isError: true);
    }

    internal static string GetCurrentLocation(State shellState)
    {
        return ShellLocation.GetCurrentLocation(shellState);
    }

    private static string? GetCommandErrorMessage(CommandState commandState)
    {
        return commandState switch
        {
            ErrorCommandState errorState => errorState.Exception.Message,
            ParserErrorCommandState parserErrorState => string.Join("; ", parserErrorState.Errors.Select(e => e.Message)),
            _ => null,
        };
    }

    private static CallToolResult CreateResponse(JsonObject payload, State shellState, bool isError)
    {
        var envelope = new JsonObject
        {
            ["currentLocation"] = GetCurrentLocation(shellState),
        };

        foreach (var kvp in payload)
        {
            envelope[kvp.Key] = kvp.Value?.DeepClone();
        }

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = envelope.ToJsonString(JsonOptions),
                }
            ],
            IsError = isError,
        };
    }

    private static JsonObject CreateSuccessPayload(CommandState commandState)
    {
        var payload = new JsonObject();

        if (commandState.IsError)
        {
            payload["error"] = GetErrorPayloadMessage(commandState);

            return payload;
        }

        var resultNode = CreateResultNode(commandState);
        if (resultNode != null)
        {
            payload["result"] = resultNode;
        }

        if (commandState.OutputFormat == OutputFormat.CSV)
        {
            var outputText = commandState.GenerateOutputText();
            if (!string.IsNullOrWhiteSpace(outputText))
            {
                payload["outputText"] = outputText;
            }
        }

        return payload;
    }

    private static JsonNode? CreateResultNode(CommandState commandState)
    {
        if (commandState.Result == null)
        {
            return null;
        }

        try
        {
            var convertedResult = commandState.Result.ConvertShellObject(DataType.Json);
            return convertedResult switch
            {
                JsonElement jsonElement => JsonSerializer.SerializeToNode(jsonElement),
                _ => JsonSerializer.SerializeToNode(convertedResult),
            };
        }
        catch (InvalidOperationException)
        {
            var textResult = commandState.Result.ConvertShellObject(DataType.Text);
            return JsonValue.Create(textResult?.ToString());
        }
    }

    private static string GetErrorPayloadMessage(CommandState commandState)
    {
        var errorMessage = GetCommandErrorMessage(commandState);
        if (!string.IsNullOrEmpty(errorMessage))
        {
            return errorMessage;
        }

        if (commandState.Result != null)
        {
            var textResult = commandState.Result.ConvertShellObject(DataType.Text)?.ToString();
            if (!string.IsNullOrWhiteSpace(textResult))
            {
                return textResult;
            }
        }

        return DefaultErrorMessage;
    }
}