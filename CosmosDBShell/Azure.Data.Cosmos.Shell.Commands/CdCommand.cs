// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

[CosmosCommand("cd")]
[CosmosExample("cd MyDatabase", Description = "Navigate into a database")]
[CosmosExample("cd MyContainer", Description = "Navigate into a container within current database")]
[CosmosExample("cd ..", Description = "Go back to parent level")]
[CosmosExample("cd MyDatabase/MyContainer", Description = "Navigate to container using full path")]
[CosmosExample("cd /MyDatabase/MyContainer", Description = "Navigate to container using absolute path")]
[CosmosExample("cd --db=MyDatabase --con=MyContainer", Description = "Navigate using options")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(Description = @"
Changes to a database or container.

The folder structure is as follows: /database/container

There is no folder inside a container.
You can go back to the parent with 'cd ..'.
To switch from one container to another you need to specify '..' as parent folder. 
Example switching to container 'foo' inside container 'bar' you need to 'cd ../foo' 
")]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class CdCommand : CosmosCommand
{
    [CosmosParameter("item", IsRequired = false, ParameterType = ParameterType.Database | ParameterType.Container)]
    public string? Item { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("quiet", "q")]
    public bool Quiet { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        // Validate: cannot specify both item and options
        bool hasOptions = !string.IsNullOrWhiteSpace(this.Database) || !string.IsNullOrWhiteSpace(this.Container);
        if (!string.IsNullOrWhiteSpace(this.Item) && hasOptions)
        {
            throw new CommandException("cd", MessageService.GetString("command-cd-error-item_and_options"));
        }

        // Get connected state - required for all cd operations
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("cd");
        }

        string? targetDatabase;
        string? targetContainer;

        if (hasOptions)
        {
            // Use options directly
            targetDatabase = this.Database;
            targetContainer = this.Container;
        }
        else
        {
            // Parse from Item path
            (targetDatabase, targetContainer) = ParsePath(this.Item, shell.State);
        }

        // Handle "cd" with no arguments - go to root
        if (targetDatabase == null && targetContainer == null)
        {
            SetState(shell, new ConnectedState(connectedState.Client));
            if (!this.Quiet)
            {
                ShellInterpreter.WriteLine(MessageService.GetString("command-cd-changed_to_connected_state"));
            }

            CosmosCompleteCommand.ClearContainers();
            return CreateResultState("connected state", "true");
        }

        // Validate and navigate to database
        if (targetDatabase != null)
        {
            await ValidateDatabaseExistsAsync(connectedState.Client, targetDatabase, "cd", token);

            if (targetContainer == null)
            {
                SetState(shell, new DatabaseState(targetDatabase, connectedState.Client));
                if (!this.Quiet)
                {
                    ShellInterpreter.WriteLine(MessageService.GetString("command-cd-changed_to_db", new Dictionary<string, object> { { "db", targetDatabase } }));
                }

                CosmosCompleteCommand.ClearContainers();
                return CreateResultState("database state", targetDatabase);
            }

            // Continue to navigate to container
            await ValidateContainerExistsAsync(connectedState.Client, targetDatabase, targetContainer, "cd", token);
            SetState(shell, new ContainerState(targetContainer, targetDatabase, connectedState.Client));
            if (!this.Quiet)
            {
                ShellInterpreter.WriteLine(MessageService.GetString("command-cd-changed_to_container", new Dictionary<string, object> { { "container", targetContainer } }));
            }

            CosmosCompleteCommand.ClearContainers();
            return CreateResultState("container state", targetContainer);
        }

        // Navigate to container (relative path from DatabaseState)
        if (targetContainer != null)
        {
            var dbName = GetCurrentDatabase(shell.State);
            if (dbName == null)
            {
                throw new NotInDatabaseException("cd");
            }

            await ValidateContainerExistsAsync(connectedState.Client, dbName, targetContainer, "cd", token);
            SetState(shell, new ContainerState(targetContainer, dbName, connectedState.Client));
            if (!this.Quiet)
            {
                ShellInterpreter.WriteLine(MessageService.GetString("command-cd-changed_to_container", new Dictionary<string, object> { { "container", targetContainer } }));
            }

            CosmosCompleteCommand.ClearContainers();
            return CreateResultState("container state", targetContainer);
        }

        return commandState;
    }

    /// <summary>
    /// Sets the shell state and synchronizes with the global interpreter state.
    /// The old state is intentionally NOT disposed because cd only navigates
    /// between levels (Connected/Database/Container) that all share the same
    /// <see cref="CosmosClient"/>. Disposing the old state would kill the client
    /// the new state still references.
    /// </summary>
    private static void SetState(ShellInterpreter shell, State newState)
    {
        shell.State = newState;
        ShellInterpreter.Instance.State = newState;
    }

    private static string? GetCurrentDatabase(State state)
    {
        return state switch
        {
            ContainerState containerState => containerState.DatabaseName,
            DatabaseState databaseState => databaseState.DatabaseName,
            _ => null,
        };
    }

    private static (string? Database, string? Container) ParsePath(string? path, State currentState)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (null, null);
        }

        // Handle ".." navigation
        if (path == "..")
        {
            return currentState switch
            {
                ContainerState containerSt => (containerSt.DatabaseName, null), // Go to database
                DatabaseState => (null, null), // Go to root
                _ => (null, null),
            };
        }

        // Check if absolute path (starts with /)
        bool isAbsolute = path.StartsWith('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            return (null, null);
        }

        // Handle relative paths with ".." segments
        var resolvedSegments = new List<string>();
        string? currentDb = GetCurrentDatabase(currentState);
        string? currentContainer = currentState is ContainerState containerState ? containerState.ContainerName : null;

        // For relative paths, start from current location
        if (!isAbsolute)
        {
            if (currentDb != null)
            {
                resolvedSegments.Add(currentDb);
            }

            if (currentContainer != null)
            {
                resolvedSegments.Add(currentContainer);
            }
        }

        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                if (resolvedSegments.Count > 0)
                {
                    resolvedSegments.RemoveAt(resolvedSegments.Count - 1);
                }
            }
            else
            {
                resolvedSegments.Add(segment);
            }
        }

        return resolvedSegments.Count switch
        {
            0 => (null, null),
            1 => (resolvedSegments[0], null),
            _ => (resolvedSegments[0], resolvedSegments[1]),
        };
    }

    private static CommandState CreateResultState(string key, string value)
    {
        var commandState = new CommandState
        {
            IsPrinted = true,
        };
        var jsonString = $"{{\"{key}\": \"{value}\"}}";
        using var jsonDoc = JsonDocument.Parse(jsonString);
        commandState.Result = new ShellJson(jsonDoc.RootElement.Clone());
        return commandState;
    }
}
