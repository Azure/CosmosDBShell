//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Reflection;
using Azure.Data.Cosmos.Shell.Mcp;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Factory class responsible for creating instances of CosmosCommand.
/// </summary>
internal class CommandFactory(Type commandType, CosmosCommandAttribute attr)
{
    /// <summary>
    /// Gets the name of the command.
    /// </summary>
    public string CommandName { get => attr.CommandName; }

    /// <summary>
    /// Gets the description of the command.
    /// </summary>
    public string? Description { get => MessageService.GetString($"command-{this.CommandName}-description"); }

    public McpAnnotationAttribute? McpAnnotation
    {
        get
        {
            return commandType.GetCustomAttribute<McpAnnotationAttribute>();
        }
    }

    /// <summary>
    /// Gets the MCP description of the command.
    /// </summary>
    public string? McpDescription { get => this.McpAnnotation?.Description; }

    /// <summary>
    /// Gets a value indicating whether the command is MCP restricted.
    /// </summary>
    public bool McpRestricted { get => this.McpAnnotation?.Restricted ?? false; }

    /// <summary>
    /// Gets the list of parameters for the command.
    /// </summary>
    public List<Parameter> Parameters { get; } = [];

    /// <summary>
    /// Gets the list of options for the command.
    /// </summary>
    public List<Option> Options { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the command is external.
    /// </summary>
    public bool IsExternal { get => attr.External; }

    /// <summary>
    /// Gets all examples for the command with their descriptions.
    /// </summary>
    public List<(string Example, string? Description)> ExamplesWithDescriptions
    {
        get
        {
            return commandType.GetCustomAttributes<CosmosExampleAttribute>()
                .Select(attr => (attr.Example, attr.Description))
                .ToList();
        }
    }

    /// <summary>
    /// Gets all examples for the command (for backward compatibility).
    /// </summary>
    public List<string> Examples
    {
        get
        {
            return commandType.GetCustomAttributes<CosmosExampleAttribute>()
                .Select(attr => attr.Example)
                .ToList();
        }
    }

    /// <summary>
    /// Attempts to create a factory for the specified type.
    /// </summary>
    /// <param name="type">The type to create the factory for.</param>
    /// <param name="factory">The created factory instance.</param>
    /// <returns>True if the factory was successfully created; otherwise, false.</returns>
    public static bool TryCreateFactory(Type type, out CommandFactory factory)
    {
        factory = null!;
        var attr = type.GetCustomAttribute<CosmosCommandAttribute>();
        if (attr == null)
        {
            return false;
        }

        factory = new CommandFactory(type, attr);
        foreach (var p in type.GetProperties())
        {
            var pattr = p.GetCustomAttribute<CosmosParameterAttribute>();
            if (pattr != null)
            {
                factory.Parameters.Add(new Parameter(p, pattr));
            }

            var optattr = p.GetCustomAttribute<CosmosOptionAttribute>();
            if (optattr != null)
            {
                factory.Options.Add(new Option(p, optattr));
            }
        }

        return true;
    }

    /// <summary>
    /// Creates an instance of the command without arguments.
    /// </summary>
    /// <returns>A new instance of <see cref="CosmosCommand"/>.</returns>
    /// <exception cref="CommandException">Thrown if the command type does not inherit from <see cref="CosmosCommand"/>.</exception>
    public CosmosCommand CreateCommand()
    {
        if (Activator.CreateInstance(commandType) is not CosmosCommand cmd)
        {
            throw new CommandException(this.CommandName, "Command must inherit from CosmosCommand");
        }

        return cmd;
    }

    /// <summary>
    /// Determines whether the factory has an option with the specified name.
    /// </summary>
    /// <param name="optionName">The name of the option to check.</param>
    /// <returns>True if the option exists; otherwise, false.</returns>
    internal bool HasOption(string optionName)
    {
        foreach (var opt in this.Options)
        {
            if (opt.MatchesArgument(optionName))
            {
                return true;
            }
        }

        return false;
    }
}