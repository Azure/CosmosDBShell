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

    public ToolOperations(IServiceProvider serviceProvider, ILogger<ToolOperations> logger)
    {
        this.logger = logger;
    }

    public McpRequestHandler<ListToolsRequestParams, ListToolsResult> ListToolsHandler => this.OnListToolsAsync;

    public McpRequestHandler<CallToolRequestParams, CallToolResult> CallToolHandler => this.OnCallToolsAsync;

    internal static Tool GetTool(CommandFactory command)
    {
        var tool = new Tool
        {
            Name = command.CommandName,
            Description = command.Description + command.McpDescription + (command.McpRestricted ? "\nWarning: This tool can't be used in MCP context. Usage is user only. Suggest using the command manually or run help for this command." : string.Empty),
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

    private ValueTask<ListToolsResult> OnListToolsAsync(
        RequestContext<ListToolsRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var tools = ShellInterpreter.Instance.App.Commands.Select(cmd => ToolOperations.GetTool(cmd.Value)).ToList();
        var arguments = new JsonObject
        {
            {
                "command",
                new JsonObject()
                {
                    ["type"] = "string",
                    ["description"] = "Command name to get help of.",
                }
            },
        };
        var listToolsResult = new ListToolsResult { Tools = tools };
        this.logger?.LogInformation($"Listing {tools.Count} tools.");
        return new ValueTask<ListToolsResult>(listToolsResult);
    }

    private async ValueTask<CallToolResult> OnCallToolsAsync(
        RequestContext<CallToolRequestParams> parameters,
        CancellationToken cancellationToken)
    {
        var requestJson = System.Text.Json.JsonSerializer.Serialize(
            parameters?.Params,
            new JsonSerializerOptions { WriteIndented = true });

        var sb = new StringBuilder();
        sb.Append(parameters?.Params?.Name);

        if (parameters?.Params == null)
        {
            var content = new TextContentBlock
            {
                Text = "Cannot call tools with null parameters.",
            };

            this.logger?.LogWarning(content.Text);

            return new CallToolResult
            {
                Content = [content],
                IsError = true,
            };
        }

        if (!ShellInterpreter.Instance.App.Commands.TryGetValue(parameters.Params.Name, out var command))
        {
            var content = new TextContentBlock
            {
                Text = $"Could not find command: {parameters.Params.Name}",
            };

            this.logger?.LogWarning(content.Text);

            return new CallToolResult
            {
                Content = [content],
                IsError = true,
            };
        }

        if (command.McpRestricted)
        {
            this.logger?.LogWarning($"Command '{command.CommandName}' is restricted for MCP.");
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = $"Command '{command.CommandName}' is restricted for MCP. Use '{command.CommandName}' manually. Suggest to run 'help {command.CommandName}'.",
                    }
                ],
                IsError = true,
            };
        }

        var cmd = command.CreateCommand();

        if (parameters.Params.Arguments != null)
        {
            foreach (var par in parameters.Params.Arguments)
            {
                if (par.Key == null)
                {
                    continue;
                }

                var argument = command.Options.FirstOrDefault(a => MatchesArgumentName(a.Name, par.Key));
                if (argument != null)
                {
                    var property = cmd.GetType().GetProperty(argument.Name[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (property != null && property.CanWrite)
                    {
                        try
                        {
                            object? convertedValue = null;
                            if (par.Value is JsonElement jsonElement)
                            {
                                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                convertedValue = ConvertJsonElement(jsonElement, targetType);
                            }
                            else
                            {
                                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                convertedValue = Convert.ChangeType(par.Value, targetType);
                            }

                            sb.Append(" /").Append(argument.Name[0]);
                            sb.Append(' ').Append(FormatParameter(convertedValue?.ToString()));
                            property.SetValue(cmd, convertedValue);
                        }
                        catch (Exception ex)
                        {
                            this.logger?.LogWarning(ex, $"Failed to set property '{argument.Name[0]}' on command '{command.CommandName}'.");
                        }
                    }

                    continue;
                }

                var parameter = command.Parameters.FirstOrDefault(a => MatchesArgumentName(a.Name, par.Key));
                if (parameter != null)
                {
                    var property = cmd.GetType().GetProperty(parameter.Name[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (property != null && property.CanWrite)
                    {
                        try
                        {
                            object? convertedValue = null;
                            if (par.Value is JsonElement jsonElement)
                            {
                                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                convertedValue = ConvertJsonElement(jsonElement, targetType);
                            }
                            else
                            {
                                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                convertedValue = Convert.ChangeType(par.Value, targetType);
                            }

                            sb.Append(' ').Append(FormatParameter(convertedValue?.ToString()));
                            property.SetValue(cmd, convertedValue);
                        }
                        catch (Exception ex)
                        {
                            this.logger?.LogWarning(ex, $"Failed to set property '{parameter.Name[0]}' on command '{command.CommandName}'.");
                        }
                    }

                    continue;
                }

                this.logger?.LogWarning($"Unknown argument '{par.Key}' for command '{command.CommandName}'. It will be ignored.");
            }
        }

        this.logger?.LogTrace($"Invoking '{command.CommandName}'.");

        try
        {
            ShellInterpreter.Instance.PrintCommand(sb.ToString());
            var response = await cmd.ExecuteAsync(ShellInterpreter.Instance, new CommandState(), command.CommandName, cancellationToken);
            var text = response.GenerateOutputText();
            ShellInterpreter.Instance.CancelPrompt();
            return new CallToolResult
            {
                Content = [
                    new TextContentBlock
                    {
                        Text = text,
                    }
                ],
            };
        }
        catch (Exception ex)
        {
            this.logger?.LogError(ex, $"An exception occurred running '{command.CommandName}'. ");

            return new CallToolResult
            {
                Content = [
                    new TextContentBlock
                    {
                        Text = $"Error executing command '{command.CommandName}': {ex.Message}",
                    }
                ],
                IsError = true,
            };
        }
        finally
        {
            this.logger?.LogTrace($"Finished executing '{command.CommandName}'.");
        }
    }
}
