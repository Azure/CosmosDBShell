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

            description += "Warning: This tool can't be used in MCP context. Usage is user only. Suggest using the command manually or run help for this command.";
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

            properties[option.Name[0]] = CreatePropertySchema(
                propertyInfo.PropertyType,
                option.GetDescription(command.CommandName),
                option.Name,
                option.DefaultValue);
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

    private static string FormatParameter(string? p)
    {
        if (p == null)
        {
            return "\"\"";
        }

        // Check if parameter needs quoting (contains spaces or special characters)
        bool needsQuoting = string.IsNullOrEmpty(p) ||
            p.Contains(' ') ||
            p.Contains('\t') ||
            p.Contains('\n') ||
            p.Contains('\r') ||
            p.Contains('"') ||
            p.Contains('\\');

        if (needsQuoting)
        {
            var sb = new StringBuilder(p.Length + 10); // Add some extra capacity for quotes and escaping
            sb.Append('"');

            // Escape double quotes and other special characters using backslash sequences
            foreach (char c in p)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        return p;
    }

    internal static string FormatOptionForHistory(Option option, object? value)
    {
        return $" --{option.Name[0]} {FormatParameter(value?.ToString())}";
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
        this.logger?.LogInformation($"Listing {tools.Count} tools.");
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

        if (command.McpRestricted)
        {
            this.logger?.LogWarning($"Command '{command.CommandName}' is restricted for MCP.");
            return McpResponseFactory.CreateError(
                $"Command '{command.CommandName}' is restricted for MCP. Use '{command.CommandName}' manually. Suggest to run 'help {command.CommandName}'.",
                ShellInterpreter.Instance.State);
        }

        var cmd = command.CreateCommand();
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
                    suppliedParameters.Add(parameter.Name[0]);
                    var bindError = this.BindMember(
                        cmd,
                        parameter.PropertyInfo,
                        par.Value,
                        memberKind: "parameter",
                        memberDisplay: parameter.Name[0],
                        commandName: command.CommandName,
                        appendToHistory: value => sb.Append(' ').Append(FormatParameter(value?.ToString())));
                    if (bindError != null)
                    {
                        return bindError;
                    }

                    continue;
                }

                var knownNames = command.Options.SelectMany(o => o.Name)
                    .Concat(command.Parameters.SelectMany(p => p.Name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
                var unknownArgMessage = $"Unknown argument '{par.Key}' for command '{command.CommandName}'. Known arguments: {string.Join(", ", knownNames)}.";
                this.logger?.LogWarning(unknownArgMessage);
                return McpResponseFactory.CreateError(unknownArgMessage, ShellInterpreter.Instance.State);
            }
        }

        var missingRequired = command.Parameters
            .Where(p => p.IsRequired && !suppliedParameters.Contains(p.Name[0]))
            .Select(p => p.Name[0])
            .ToList();
        if (missingRequired.Count > 0)
        {
            var missingMessage = $"Missing required parameter(s) for command '{command.CommandName}': {string.Join(", ", missingRequired)}.";
            this.logger?.LogWarning(missingMessage);
            return McpResponseFactory.CreateError(missingMessage, ShellInterpreter.Instance.State);
        }

        this.logger?.LogTrace($"Invoking '{command.CommandName}'.");

        try
        {
            ShellInterpreter.Instance.PrintCommand(sb.ToString());
            var response = await cmd.ExecuteAsync(ShellInterpreter.Instance, new CommandState(), command.CommandName, cancellationToken);
            ShellInterpreter.Instance.CancelPrompt();
            return McpResponseFactory.CreateSuccess(response, ShellInterpreter.Instance.State);
        }
        catch (Exception ex)
        {
            this.logger?.LogError(ex, $"An exception occurred running '{command.CommandName}'. ");

            return McpResponseFactory.CreateError($"Error executing command '{command.CommandName}': {ex.Message}", ShellInterpreter.Instance.State);
        }
        finally
        {
            this.logger?.LogTrace($"Finished executing '{command.CommandName}'.");
        }
    }
}
