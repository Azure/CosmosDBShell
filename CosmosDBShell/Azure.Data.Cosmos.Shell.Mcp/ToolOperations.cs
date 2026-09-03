// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

internal class ToolOperations
{
    internal const int DefaultPageSize = ResultLimit.DefaultMaxItemCount;

    private const string ContinuationArgument = "continuation";

    private const string MaxArgument = "max";

    private const string ContinuationDescription =
        "Non-null continuation token returned by a previous call to this tool. Pass it back to fetch the next page, or omit this argument to start from the beginning. A null output token means the result is exhausted and no further call should be made. The value is opaque; do not modify it.";

    private readonly ILogger<ToolOperations> logger;
    private readonly Lazy<List<Tool>> cachedTools;

    public ToolOperations(ILogger<ToolOperations> logger)
    {
        this.logger = logger;
        this.cachedTools = new Lazy<List<Tool>>(
            () => ShellInterpreter.Instance.App.Commands.Values
                .DistinctBy(c => c.CommandName)
                .Select(GetTool)
                .ToList(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public McpRequestHandler<ListToolsRequestParams, ListToolsResult> ListToolsHandler => this.OnListToolsAsync;

    public McpRequestHandler<CallToolRequestParams, CallToolResult> CallToolHandler => this.OnCallToolsAsync;

    internal static Tool GetTool(CommandFactory command)
    {
        var descriptionParts = new[] { command.Description, command.McpDescription }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var description = string.Join("\n", descriptionParts);

        if (command.McpRestricted)
        {
            if (description.Length > 0)
            {
                description += "\n";
            }

            if (RequiresConfirmation(command))
            {
                description += "Warning: This command is destructive. When invoked through MCP it always requires explicit user confirmation before it runs — even when a force or no-prompt argument is supplied. If the client cannot confirm, the command is refused.";
            }
            else
            {
                description += "Warning: This command is restricted to interactive use and cannot be invoked through MCP. Run it manually in the shell, or call 'help' for this command.";
            }
        }

        var tool = new Tool
        {
            Name = command.CommandName,
            Description = description,
        };
        var schema = new JsonObject
        {
            ["type"] = "object",
        };
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var arg in command.Parameters)
        {
            if (arg.IsRequired)
            {
                required.Add(arg.Name[0]);
            }

            var propertyInfo = arg.PropertyInfo
                ?? throw new InvalidOperationException($"Parameter '{arg.Name[0]}' for command '{command.CommandName}' is missing property metadata.");

            properties[arg.Name[0]] = CreatePropertySchema(
                propertyInfo.PropertyType,
                arg.GetDescription(command.CommandName),
                arg.Name);
        }

        foreach (var option in command.Options)
        {
            var propertyInfo = option.PropertyInfo
                ?? throw new InvalidOperationException($"Option '{option.Name[0]}' for command '{command.CommandName}' is missing property metadata.");

            var propertySchema = CreatePropertySchema(
                propertyInfo.PropertyType,
                GetMcpOptionDescription(command, option),
                option.Name,
                GetMcpDefaultValue(command, option));
            if (IsPagedMaxOption(command, option))
            {
                propertySchema["minimum"] = 1;
            }

            properties[option.Name[0]] = propertySchema;
        }

        if (command.IsPaged)
        {
            properties[ContinuationArgument] = CreatePropertySchema(
                typeof(string),
                ContinuationDescription,
                [ContinuationArgument]);
        }

        if (properties.Count > 0)
        {
            schema["properties"] = properties;
        }

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        var mcpAnnotation = command.McpAnnotation;

        if (mcpAnnotation != null)
        {
            var annotation = new ToolAnnotations { Title = mcpAnnotation.Title };
            if (mcpAnnotation.ReadOnly)
            {
                annotation.ReadOnlyHint = true;
            }

            if (mcpAnnotation.Destructive)
            {
                annotation.DestructiveHint = true;
            }

            if (mcpAnnotation.Idempotent)
            {
                annotation.IdempotentHint = true;
            }

            if (mcpAnnotation.OpenWorld)
            {
                annotation.OpenWorldHint = true;
            }

            tool.Annotations = annotation;
        }

        tool.InputSchema = JsonSerializer.SerializeToElement(schema);
        return tool;
    }

    private static object? ConvertJsonElement(JsonElement jsonElement, Type targetType)
    {
        if (targetType == typeof(string))
        {
            return jsonElement.GetString();
        }

        if (targetType == typeof(int))
        {
            return jsonElement.GetInt32();
        }

        if (targetType == typeof(bool))
        {
            return jsonElement.GetBoolean();
        }

        if (targetType == typeof(double))
        {
            return jsonElement.GetDouble();
        }

        if (targetType.IsEnum)
        {
            var stringValue = jsonElement.GetString();
            if (stringValue != null && Enum.TryParse(targetType, stringValue, true, out var parsedEnum))
            {
                return parsedEnum;
            }
        }

        return JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType);
    }

    internal static bool MatchesArgumentName(string[] names, string? argumentName)
    {
        return names.Any(name => name.Equals(argumentName, StringComparison.OrdinalIgnoreCase));
    }

    internal static string FormatOptionForHistory(Option option, object? value)
    {
        return $" --{option.Name[0]} {ShellLiteral.Quote(value?.ToString())}";
    }

    internal static void ConfigurePaging(object command)
    {
        if (command is IPagedCommand paged)
        {
            paged.IsMcpRequest = true;
        }
    }

    internal static bool TrySetContinuation(object command, string argumentName, JsonElement value, out string? errorMessage)
    {
        errorMessage = null;
        if (command is not IPagedCommand paged
            || !argumentName.Equals(ContinuationArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.ValueKind is not JsonValueKind.String)
        {
            errorMessage = "Invalid value for MCP argument 'continuation'. Expected a non-null string.";
            return true;
        }

        paged.Continuation = value.GetString();
        return true;
    }

    private static object? GetMcpDefaultValue(CommandFactory command, Option option)
    {
        return IsPagedMaxOption(command, option)
            ? DefaultPageSize
            : option.DefaultValue;
    }

    // The shared description states the shell's whole-result semantics, which differ under MCP.
    private static string? GetMcpOptionDescription(CommandFactory command, Option option)
    {
        var description = option.GetDescription(command.CommandName);
        return IsPagedMaxOption(command, option)
            ? $"{description} Through MCP this must be positive and bounds a single page rather than the whole result set. Omitted or non-positive values use the default of {DefaultPageSize}. A call can return fewer items and still have more available; use continuationToken to detect the end."
            : description;
    }

    private static bool IsPagedMaxOption(CommandFactory command, Option option)
    {
        return command.IsPaged && option.Name.Contains(MaxArgument, StringComparer.OrdinalIgnoreCase);
    }

    private static JsonObject CreatePropertySchema(Type propertyType, string? description, string[] names, object? defaultValue = null)
    {
        var schema = CreateTypeSchema(propertyType);
        var descriptionText = description ?? string.Empty;

        if (names.Length > 1)
        {
            var aliases = string.Join(", ", names.Skip(1));
            descriptionText = string.IsNullOrWhiteSpace(descriptionText)
                ? $"Aliases: {aliases}"
                : $"{descriptionText} Aliases: {aliases}";
        }

        if (!string.IsNullOrWhiteSpace(descriptionText))
        {
            schema["description"] = descriptionText;
        }

        if (defaultValue != null)
        {
            var serializedDefault = defaultValue is Enum
                ? JsonValue.Create(Enum.GetName(defaultValue.GetType(), defaultValue))
                : JsonSerializer.SerializeToNode(defaultValue);
            schema["default"] = serializedDefault;
        }

        return schema;
    }

    private static JsonObject CreateTypeSchema(Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (targetType.IsEnum)
        {
            var enumValues = new JsonArray();
            foreach (var name in Enum.GetNames(targetType))
            {
                enumValues.Add(name);
            }

            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = enumValues,
            };
        }

        if (targetType.IsArray)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = CreateTypeSchema(targetType.GetElementType() ?? typeof(string)),
            };
        }

        return new JsonObject
        {
            ["type"] = GetJsonSchemaType(targetType),
        };
    }

    private static string GetJsonSchemaType(Type targetType)
    {
        if (targetType == typeof(bool))
        {
            return "boolean";
        }

        if (targetType == typeof(int) ||
            targetType == typeof(long) ||
            targetType == typeof(short))
        {
            return "integer";
        }

        if (targetType == typeof(float) ||
            targetType == typeof(double) ||
            targetType == typeof(decimal))
        {
            return "number";
        }

        return "string";
    }

    private static bool RequiresConfirmation(CommandFactory command)
    {
        var annotation = command.McpAnnotation;
        return command.McpRestricted
            && annotation is not null
            && annotation.Confirmable
            && annotation.Destructive;
    }

    private CallToolResult? BindMember(
        object cmd,
        PropertyInfo property,
        object? rawValue,
        string memberKind,
        string memberDisplay,
        string commandName,
        Action<object?> appendToHistory)
    {
        if (property == null || !property.CanWrite)
        {
            return null;
        }

        object? convertedValue;
        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        try
        {
            convertedValue = rawValue is JsonElement jsonElement
                ? ConvertJsonElement(jsonElement, targetType)
                : Convert.ChangeType(rawValue, targetType);
        }
        catch (Exception ex)
        {
            // Do not include the exception message or the raw value: conversion
            // failures often echo the offending input (e.g. an AccountKey), which
            // would leak secrets into both the log and the MCP error response.
            var errorMessage = $"Invalid value for {memberKind} '{memberDisplay}' on command '{commandName}'. Expected a value of type '{targetType.Name}'.";
            this.logger?.LogWarning("{Message} (conversion threw {ExceptionType})", errorMessage, ex.GetType().Name);
            return McpResponseFactory.CreateError(errorMessage, ShellInterpreter.Instance.State);
        }

        try
        {
            appendToHistory(convertedValue);
            property.SetValue(cmd, convertedValue);
        }
        catch (Exception ex)
        {
            // Do not include the exception message: a property setter may throw a
            // validation exception that echoes the provided value, which could leak
            // secrets into the log and the MCP error response.
            var errorMessage = $"Failed to set {memberKind} '{memberDisplay}' on command '{commandName}'.";
            this.logger?.LogWarning("{Message} (setter threw {ExceptionType})", errorMessage, ex.GetType().Name);
            return McpResponseFactory.CreateError(errorMessage, ShellInterpreter.Instance.State);
        }

        return null;
    }

    private ValueTask<ListToolsResult> OnListToolsAsync(
        RequestContext<ListToolsRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var tools = this.cachedTools.Value;
        var listToolsResult = new ListToolsResult { Tools = tools };
        this.logger?.LogInformation("Listing {ToolCount} tools.", tools.Count);
        return new ValueTask<ListToolsResult>(listToolsResult);
    }

    private async ValueTask<CallToolResult> OnCallToolsAsync(
        RequestContext<CallToolRequestParams> parameters,
        CancellationToken cancellationToken)
    {
        if (this.logger?.IsEnabled(LogLevel.Trace) == true)
        {
            var argumentNames = parameters?.Params?.Arguments == null
                ? "(none)"
                : string.Join(", ", parameters.Params.Arguments.Keys);
            this.logger.LogTrace(
                "MCP CallTool request: tool={Tool}, arguments=[{Arguments}]",
                parameters?.Params?.Name,
                argumentNames);
        }

        var sb = new StringBuilder();
        sb.Append(parameters?.Params?.Name);

        if (parameters?.Params == null)
        {
            this.logger?.LogWarning("Cannot call tools with null parameters.");

            return McpResponseFactory.CreateError("Cannot call tools with null parameters.", ShellInterpreter.Instance.State);
        }

        if (!ShellInterpreter.Instance.App.Commands.TryGetValue(parameters.Params.Name, out var command))
        {
            var errorMessage = $"Could not find command: {parameters.Params.Name}";

            this.logger?.LogWarning(errorMessage);

            return McpResponseFactory.CreateError(errorMessage, ShellInterpreter.Instance.State);
        }

        if (command.McpRestricted && !RequiresConfirmation(command))
        {
            this.logger?.LogWarning($"Command '{command.CommandName}' is restricted for MCP.");
            return McpResponseFactory.CreateError(
                $"Command '{command.CommandName}' is restricted for MCP. Use '{command.CommandName}' manually. Suggest to run 'help {command.CommandName}'.",
                ShellInterpreter.Instance.State);
        }

        var cmd = command.CreateCommand();
        ConfigurePaging(cmd);
        var suppliedParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (parameters.Params.Arguments != null)
        {
            foreach (var par in parameters.Params.Arguments)
            {
                if (par.Key == null)
                {
                    continue;
                }

                var option = command.Options.FirstOrDefault(a => MatchesArgumentName(a.Name, par.Key));
                if (option != null)
                {
                    var bindError = this.BindMember(
                        cmd,
                        option.PropertyInfo,
                        par.Value,
                        memberKind: "option",
                        memberDisplay: $"--{option.Name[0]}",
                        commandName: command.CommandName,
                        appendToHistory: value => sb.Append(FormatOptionForHistory(option, value)));
                    if (bindError != null)
                    {
                        return bindError;
                    }

                    continue;
                }

                var parameter = command.Parameters.FirstOrDefault(a => MatchesArgumentName(a.Name, par.Key));
                if (parameter != null)
                {
                    var bindError = this.BindMember(
                        cmd,
                        parameter.PropertyInfo,
                        par.Value,
                        memberKind: "parameter",
                        memberDisplay: parameter.Name[0],
                        commandName: command.CommandName,
                        appendToHistory: value => sb.Append(' ').Append(ShellLiteral.Quote(value?.ToString())));
                    if (bindError != null)
                    {
                        return bindError;
                    }

                    var boundValue = parameter.PropertyInfo.GetValue(cmd);
                    if (boundValue != null
                        && (boundValue is not string stringValue || !string.IsNullOrWhiteSpace(stringValue)))
                    {
                        suppliedParameters.Add(parameter.Name[0]);
                    }

                    continue;
                }

                // Kept out of the echoed command line: tokens are large and add no diagnostic value.
                if (TrySetContinuation(cmd, par.Key, par.Value, out var continuationError))
                {
                    if (continuationError != null)
                    {
                        this.logger?.LogWarning("{Message}", continuationError);
                        return McpResponseFactory.CreateError(continuationError, ShellInterpreter.Instance.State);
                    }

                    continue;
                }

                var knownNames = command.Options.SelectMany(o => o.Name)
                    .Concat(command.Parameters.SelectMany(p => p.Name))
                    .Concat(command.IsPaged ? new[] { ContinuationArgument } : Array.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
                var unknownArgMessage = $"Unknown argument '{par.Key}' for command '{command.CommandName}'. Known arguments: {string.Join(", ", knownNames)}.";
                this.logger?.LogWarning(unknownArgMessage);
                return McpResponseFactory.CreateError(unknownArgMessage, ShellInterpreter.Instance.State);
            }
        }

        var missingRequired = command.Parameters
            .Where(p => p.IsRequired && !suppliedParameters.Contains(p.Name[0]))
            .ToList();
        if (missingRequired.Count > 0)
        {
            var missingDetails = missingRequired.Select(p =>
                !string.IsNullOrEmpty(p.RequiredErrorKey)
                    ? MessageService.GetString(p.RequiredErrorKey)
                    : p.Name[0]);
            var missingMessage = $"Missing required parameter(s) for command '{command.CommandName}': {string.Join("; ", missingDetails)}.";
            this.logger?.LogWarning("Missing required parameter(s) for command {CommandName}.", command.CommandName);
            return McpResponseFactory.CreateError(missingMessage, ShellInterpreter.Instance.State);
        }

        var batchSubcommand = (cmd as BatchCommand)?.Subcommand?.Trim();
        if (!string.IsNullOrEmpty(batchSubcommand)
            && !string.Equals(batchSubcommand, "run", StringComparison.OrdinalIgnoreCase))
        {
            const string errorMessage = "MCP supports only the stateless 'batch run' subcommand. Run stateful batch commands manually in the shell.";
            this.logger?.LogWarning(errorMessage);
            return McpResponseFactory.CreateError(errorMessage, ShellInterpreter.Instance.State);
        }

        if (RequiresConfirmation(command))
        {
            var server = parameters.Server;
            Func<ElicitRequestParams, CancellationToken, ValueTask<ElicitResult>>? elicit =
                server?.ClientCapabilities?.Elicitation != null ? server.ElicitAsync : null;

            var confirmation = await this.ConfirmDestructiveAsync(elicit, command.CommandName, sb.ToString(), cancellationToken);
            if (confirmation != null)
            {
                return confirmation;
            }
        }

        this.logger?.LogTrace($"Invoking '{command.CommandName}'.");

        try
        {
            ShellInterpreter.Instance.PrintCommand(sb.ToString());
            var response = await ShellInterpreter.Instance.ExecuteCosmosCommandAsync(cmd, new CommandState(), command.CommandName, cancellationToken);
            ShellInterpreter.Instance.CancelPrompt();
            return McpResponseFactory.CreateSuccess(response, ShellInterpreter.Instance.State);
        }
        catch (Exception ex)
        {
            this.logger?.LogError(ex, $"An exception occurred running '{command.CommandName}'. ");

            return McpResponseFactory.CreateError(
                $"Error executing command '{command.CommandName}': {ex.Message}",
                ShellInterpreter.Instance.State,
                RequestChargeContext.GetExceptionCharge(ex));
        }
        finally
        {
            this.logger?.LogTrace($"Finished executing '{command.CommandName}'.");
        }
    }

    // Gates a destructive command behind an MCP elicitation confirmation. Returns
    // null when the operation is approved and should proceed; otherwise returns the
    // CallToolResult to send back (refusal, denial, or a failed confirmation).
    // Fails closed: when the client cannot elicit, the command is refused rather
    // than executed.
    internal async ValueTask<CallToolResult?> ConfirmDestructiveAsync(
        Func<ElicitRequestParams, CancellationToken, ValueTask<ElicitResult>>? elicit,
        string commandName,
        string commandLine,
        CancellationToken cancellationToken)
    {
        if (elicit == null)
        {
            this.logger?.LogWarning(
                "Destructive command '{Command}' requires confirmation, but the MCP client does not support elicitation.",
                commandName);
            return McpResponseFactory.CreateError(
                $"Command '{commandName}' is destructive and needs your confirmation, but this MCP client does not support confirmation prompts (elicitation). Run '{commandLine}' manually in the shell.",
                ShellInterpreter.Instance.State);
        }

        var request = new ElicitRequestParams
        {
            Message =
                $"Confirm destructive operation. The agent wants to run: {commandLine}\n" +
                "This can permanently change or delete data in the connected Azure Cosmos DB account and cannot be undone. Approve this operation?",
            RequestedSchema = new ElicitRequestParams.RequestSchema(),
        };

        ElicitResult result;
        try
        {
            result = await elicit(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.logger?.LogWarning(ex, "Confirmation prompt for destructive command '{Command}' failed.", commandName);
            return McpResponseFactory.CreateError(
                $"Confirmation for '{commandName}' could not be completed ({ex.GetType().Name}). Nothing was executed.",
                ShellInterpreter.Instance.State);
        }

        if (!result.IsAccepted)
        {
            var action = string.IsNullOrEmpty(result.Action) ? "cancel" : result.Action;
            this.logger?.LogInformation(
                "User did not approve destructive command '{Command}' (action={Action}).",
                commandName,
                action);
            return McpResponseFactory.CreateError(
                $"Operation '{commandName}' was not approved by the user (action: {action}). Nothing was executed.",
                ShellInterpreter.Instance.State);
        }

        this.logger?.LogInformation("User approved destructive command '{Command}'.", commandName);
        return null;
    }
}
