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

    private static Tool GetTool(CommandFactory command)
    {
        var tool = new Tool
        {
            Name = command.CommandName,
            Description = command.Description + command.McpDescription + (command.McpRestricted ? "\nWarning: This tool can't be used in MCP context. Usage is user only. Suggest using the command manually or run help for this command." : string.Empty),
        };
        var args = command.Parameters;
        var schema = new JsonObject
        {
            ["type"] = "object",
        };

        if (args != null && args.Count > 0)
        {
            var arguments = new JsonObject();
            var required = new JsonArray();
            foreach (var arg in args)
            {
                if (arg.IsRequired)
                {
                    required.Add(arg.Name[0]);
                }

                arguments.Add(arg.Name[0], new JsonObject()
                {
                    ["type"] = GetParameterType(arg.ParameterType),
                    ["description"] = arg.GetDescription(command.CommandName),
                });
            }

            schema["properties"] = arguments;
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

    private static string GetParameterType(ParameterType parameterType)
    {
        // ATM only string is supported the types are used for highlighting and mcs doesn't know/want to know about a parameter
        // is a database or a container
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

                var argument = command.Options.FirstOrDefault(a => a.Name[0].Equals(par.Key, StringComparison.OrdinalIgnoreCase));
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
                                if (targetType == typeof(string))
                                {
                                    convertedValue = jsonElement.GetString();
                                }
                                else if (targetType == typeof(int))
                                {
                                    convertedValue = jsonElement.GetInt32();
                                }
                                else if (targetType == typeof(bool))
                                {
                                    convertedValue = jsonElement.GetBoolean();
                                }
                                else if (targetType == typeof(double))
                                {
                                    convertedValue = jsonElement.GetDouble();
                                }
                                else
                                {
                                    convertedValue = JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType);
                                }
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

                var parameter = command.Parameters.FirstOrDefault(a => a.Name[0].Equals(par.Key, StringComparison.OrdinalIgnoreCase));
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
                                if (targetType == typeof(string))
                                {
                                    convertedValue = jsonElement.GetString();
                                }
                                else if (targetType == typeof(int))
                                {
                                    convertedValue = jsonElement.GetInt32();
                                }
                                else if (targetType == typeof(bool))
                                {
                                    convertedValue = jsonElement.GetBoolean();
                                }
                                else if (targetType == typeof(double))
                                {
                                    convertedValue = jsonElement.GetDouble();
                                }
                                else
                                {
                                    convertedValue = JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType);
                                }
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
