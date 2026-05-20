// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

internal abstract class CosmosCommand
{
    public abstract Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token);

    protected static void ThrowNotConnected(string commandName) => throw new NotConnectedException(commandName);

    protected static void ThrowNotInDatabase(string commandName) => throw new NotInDatabaseException(commandName);

    protected static void ThrowNotInContainer(string commandName) => throw new NotInContainerException(commandName);

    /// <summary>
    /// Resolves and validates a container based on command options and current shell state.
    /// This helper consolidates the common pattern of determining which database/container to use.
    /// </summary>
    /// <param name="client">The Cosmos client.</param>
    /// <param name="state">The current shell state.</param>
    /// <param name="databaseOption">The database name from command option (--database), or null.</param>
    /// <param name="containerOption">The container name from command option (--container), or null.</param>
    /// <param name="commandName">The name of the command (for error reporting).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A tuple containing the resolved database name, container name, and Container reference.</returns>
    /// <exception cref="CommandException">Thrown when the container cannot be resolved or doesn't exist.</exception>
    protected static async Task<(string? DatabaseName, string? ContainerName, Container Container)> ResolveContainerAsync(
        CosmosClient client,
        State state,
        string? databaseOption,
        string? containerOption,
        string commandName,
        CancellationToken token)
    {
        string? databaseName = null;
        string? containerName = null;

        // Resolve database and container names based on state and options
        switch (state)
        {
            case ContainerState containerState:
                databaseName = databaseOption ?? containerState.DatabaseName;
                containerName = containerOption ?? containerState.ContainerName;
                break;

            case DatabaseState databaseState:
                databaseName = databaseOption ?? databaseState.DatabaseName;
                containerName = containerOption;
                break;

            case ConnectedState:
                databaseName = databaseOption;
                containerName = containerOption;
                break;

            default:
                ThrowNotConnected(commandName);
                break;
        }

        // Validate we have both database and container
        if (string.IsNullOrEmpty(databaseName) || string.IsNullOrEmpty(containerName))
        {
            ThrowNotInContainer(commandName);
        }

        // Validate that database and container exist
        await ValidateContainerExistsAsync(RequireConnectedState(state, commandName), databaseName, containerName, commandName, token);

        var container = client.GetDatabase(databaseName).GetContainer(containerName);
        return (databaseName, containerName, container);
    }

    /// <summary>
    /// Resolves and validates a database based on command options and current shell state.
    /// </summary>
    /// <param name="client">The Cosmos client.</param>
    /// <param name="state">The current shell state.</param>
    /// <param name="databaseOption">The database name from command option (--database), or null.</param>
    /// <param name="commandName">The name of the command (for error reporting).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A tuple containing the resolved database name and Database reference.</returns>
    /// <exception cref="CommandException">Thrown when the database cannot be resolved or doesn't exist.</exception>
    protected static async Task<(string DatabaseName, Database Database)> ResolveDatabaseAsync(
        CosmosClient client,
        State state,
        string? databaseOption,
        string commandName,
        CancellationToken token)
    {
        string? databaseName = null;

        // Resolve database name based on state and options
        switch (state)
        {
            case ContainerState containerState:
                databaseName = databaseOption ?? containerState.DatabaseName;
                break;

            case DatabaseState databaseState:
                databaseName = databaseOption ?? databaseState.DatabaseName;
                break;

            case ConnectedState:
                databaseName = databaseOption;
                break;

            default:
                ThrowNotConnected(commandName);
                break;
        }

        // Validate we have a database
        if (string.IsNullOrEmpty(databaseName))
        {
            ThrowNotInDatabase(commandName);
        }

        // Validate that database exists
        await ValidateDatabaseExistsAsync(RequireConnectedState(state, commandName), databaseName, commandName, token);

        var database = client.GetDatabase(databaseName);
        return (databaseName ?? string.Empty, database);
    }

    /// <summary>
    /// Validates that the specified database exists.
    /// </summary>
    /// <param name="client">The Cosmos client.</param>
    /// <param name="databaseName">The name of the database to validate.</param>
    /// <param name="commandName">The name of the command requesting validation (for error reporting).</param>
    /// <param name="token">Cancellation token.</param>
    /// <exception cref="CommandException">Thrown when the database does not exist.</exception>
    protected static async Task ValidateDatabaseExistsAsync(ConnectedState state, string? databaseName, string commandName, CancellationToken token)
    {
        if (databaseName == null)
        {
            return;
        }

        if (!await CosmosResourceFacade.DatabaseExistsAsync(state, databaseName, token))
        {
            throw new CommandException(
                commandName,
                MessageService.GetArgsString("error-database_not_found", "database", databaseName));
        }
    }

    /// <summary>
    /// Validates that the specified container exists in the specified database.
    /// </summary>
    /// <param name="client">The Cosmos client.</param>
    /// <param name="databaseName">The name of the database containing the container.</param>
    /// <param name="containerName">The name of the container to validate.</param>
    /// <param name="commandName">The name of the command requesting validation (for error reporting).</param>
    /// <param name="token">Cancellation token.</param>
    /// <exception cref="CommandException">Thrown when the database or container does not exist.</exception>
    protected static async Task ValidateContainerExistsAsync(ConnectedState state, string? databaseName, string? containerName, string commandName, CancellationToken token)
    {
        if (databaseName == null)
        {
            return;
        }

        // First validate the database exists
        await ValidateDatabaseExistsAsync(state, databaseName, commandName, token);

        if (containerName == null)
        {
            return;
        }

        if (!await CosmosResourceFacade.ContainerExistsAsync(state, databaseName, containerName, token))
        {
            throw new CommandException(
                commandName,
                MessageService.GetArgsString(
                    "error-container_not_found",
                    "container",
                    containerName,
                    "database",
                    databaseName));
        }
    }

    protected static IAsyncEnumerable<string> EnumerateDatabaseNamesAsync(ConnectedState state, string commandName, CancellationToken token)
    {
        _ = commandName;
        return CosmosResourceFacade.GetDatabaseNamesAsync(state, token);
    }

    protected static IAsyncEnumerable<string> EnumerateContainerNamesAsync(ConnectedState state, string databaseName, string commandName, CancellationToken token)
    {
        _ = commandName;
        return CosmosResourceFacade.GetContainerNamesAsync(state, databaseName, token);
    }

    private static ConnectedState RequireConnectedState(State state, string commandName)
    {
        if (state is ConnectedState connectedState)
        {
            return connectedState;
        }

        ThrowNotConnected(commandName);
        throw new InvalidOperationException();
    }

    protected static async IAsyncEnumerable<T> EnumerateFeedAsync<T>(FeedIterator<T> feedIterator)
    {
        while (feedIterator.HasMoreResults)
        {
            var response = await feedIterator.ReadNextAsync();
            foreach (var container in response)
            {
                yield return container;
            }
        }
    }

    /// <summary>
    /// Gets the string representation of a JSON element value for pattern matching.
    /// </summary>
    /// <param name="element">The JSON element to convert.</param>
    /// <returns>A string representation of the value.</returns>
    protected static string GetValueAsString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText(),
        };
    }

    /// <summary>
    /// Creates a PartitionKey from a JsonElement, preserving the correct type.
    /// </summary>
    /// <param name="element">The JSON element containing the partition key value.</param>
    /// <returns>A PartitionKey with the correct type.</returns>
    protected static PartitionKey CreatePartitionKey(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => new PartitionKey(element.GetString()),
            JsonValueKind.Number =>
                element.TryGetInt32(out int intValue) ? new PartitionKey(intValue) :
                element.TryGetInt64(out long longValue) ? new PartitionKey(longValue) :
                new PartitionKey(element.GetDouble()),
            JsonValueKind.True => new PartitionKey(true),
            JsonValueKind.False => new PartitionKey(false),
            JsonValueKind.Null => PartitionKey.Null,
            _ => new PartitionKey(element.GetRawText()),
        };
    }

    protected static PartitionKey CreatePartitionKey(IReadOnlyList<JsonElement> elements)
    {
        if (elements.Count == 1)
        {
            return CreatePartitionKey(elements[0]);
        }

        var builder = new PartitionKeyBuilder();
        foreach (var element in elements)
        {
            AddPartitionKeyValue(builder, element);
        }

        return builder.Build();
    }

    private static void AddPartitionKeyValue(PartitionKeyBuilder builder, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Add(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt32(out int intValue))
                {
                    builder.Add(intValue);
                }
                else if (element.TryGetInt64(out long longValue))
                {
                    builder.Add(longValue);
                }
                else
                {
                    builder.Add(element.GetDouble());
                }

                break;
            case JsonValueKind.True:
                builder.Add(true);
                break;
            case JsonValueKind.False:
                builder.Add(false);
                break;
            case JsonValueKind.Null:
                builder.AddNullValue();
                break;
            default:
                builder.Add(element.GetRawText());
                break;
        }
    }

    internal static string[] GetPartitionKeyPropertyNames(IEnumerable<string> partitionKeyPaths)
    {
        return partitionKeyPaths
            .Select(path => path.TrimStart('/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    internal static bool MatchesAnyPath(JsonElement element, IEnumerable<string> propertyPaths, PatternMatcher matcher)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (TryGetNestedProperty(element, propertyPath, out var matchKeyElement) && matcher.Match(GetValueAsString(matchKeyElement)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a PartitionKey from multiple JSON components, preserving each component type.
    /// </summary>
    /// <param name="elements">The JSON elements containing partition key component values.</param>
    /// <returns>A PartitionKey with the supplied component values.</returns>
    protected static PartitionKey CreatePartitionKey(IEnumerable<JsonElement> elements)
    {
        var builder = new PartitionKeyBuilder();
        foreach (var element in elements)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    builder.Add(element.GetString());
                    break;
                case JsonValueKind.Number:
                    // Use the most precise numeric type available so 64-bit
                    // integer key components above 2^53 are not silently
                    // rounded by GetDouble().
                    if (element.TryGetInt64(out var longValue))
                    {
                        builder.Add(longValue);
                    }
                    else
                    {
                        builder.Add(element.GetDouble());
                    }

                    break;
                case JsonValueKind.True:
                    builder.Add(true);
                    break;
                case JsonValueKind.False:
                    builder.Add(false);
                    break;
                case JsonValueKind.Null:
                    builder.AddNullValue();
                    break;
                default:
                    builder.Add(element.GetRawText());
                    break;
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a PartitionKey from a shell argument. JSON scalar literals preserve their
    /// JSON type, and JSON arrays represent hierarchical partition key components.
    /// </summary>
    /// <param name="rawValue">The raw shell argument value.</param>
    /// <returns>A typed PartitionKey.</returns>
    protected static PartitionKey CreatePartitionKeyFromArgument(string rawValue)
    {
        var trimmed = rawValue.Trim();
        if (LooksLikeJsonLiteral(trimmed))
        {
            // Container/object literals must be valid JSON: rejecting them is
            // the whole point of this check, so let the JsonException surface.
            // Scalar candidates (digits, '-', or unquoted bool/null words),
            // however, can be ambiguous: '001', '-svc', and similar are valid
            // string partition keys even though they look numeric. Fall back
            // to a string PartitionKey when JSON parsing fails for those.
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(trimmed);
            }
            catch (JsonException) when (trimmed[0] is not ('[' or '{' or '"'))
            {
                return new PartitionKey(rawValue);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    return CreatePartitionKey(root.EnumerateArray());
                }

                // Reject object-shaped JSON: a JSON object cannot represent a
                // partition key value. Surface this as a JsonException so callers
                // can translate it into their localized invalid-PK message.
                if (root.ValueKind == JsonValueKind.Object)
                {
                    throw new JsonException("Partition key cannot be a JSON object.");
                }

                return CreatePartitionKey(root);
            }
        }

        return new PartitionKey(rawValue);
    }

    /// <summary>
    /// Gets normalized partition key paths from container metadata.
    /// </summary>
    /// <param name="properties">The container properties.</param>
    /// <returns>Partition key paths without leading slashes.</returns>
    protected static IReadOnlyList<string> GetPartitionKeyPaths(ContainerProperties properties)
    {
        if (properties.PartitionKeyPaths is { Count: > 0 })
        {
            return properties.PartitionKeyPaths.Select(path => path.TrimStart('/')).ToArray();
        }

        return string.IsNullOrWhiteSpace(properties.PartitionKeyPath)
            ? []
            : [properties.PartitionKeyPath.TrimStart('/')];
    }

    /// <summary>
    /// Creates a PartitionKey from an item using the container partition key definition.
    /// </summary>
    /// <param name="item">The item JSON.</param>
    /// <param name="partitionKeyPaths">The normalized partition key paths.</param>
    /// <param name="missingPath">The missing path when the method returns false.</param>
    /// <param name="partitionKey">The resulting partition key.</param>
    /// <returns>True if all key components were found; otherwise false.</returns>
    protected static bool TryCreatePartitionKeyFromItem(JsonElement item, IReadOnlyList<string> partitionKeyPaths, out string? missingPath, out PartitionKey partitionKey)
    {
        var components = new List<JsonElement>();
        foreach (var path in partitionKeyPaths)
        {
            if (!TryGetNestedProperty(item, path, out var keyComponent))
            {
                missingPath = path;
                partitionKey = default;
                return false;
            }

            components.Add(keyComponent);
        }

        missingPath = null;
        partitionKey = components.Count == 1 ? CreatePartitionKey(components[0]) : CreatePartitionKey(components);
        return true;
    }

    private static bool LooksLikeJsonLiteral(string trimmed)
    {
        if (trimmed.Length == 0)
        {
            return false;
        }

        var first = trimmed[0];
        if (first == '[' || first == '{' || first == '"' || first == '-' || char.IsDigit(first))
        {
            return true;
        }

        return string.Equals(trimmed, "true", StringComparison.Ordinal)
            || string.Equals(trimmed, "false", StringComparison.Ordinal)
            || string.Equals(trimmed, "null", StringComparison.Ordinal);
    }

    /// <summary>
    /// Tries to get a property value from a JSON element, supporting nested paths like "nested/prop".
    /// </summary>
    /// <param name="element">The JSON element to search.</param>
    /// <param name="path">The property path (e.g., "prop" or "nested/prop").</param>
    /// <param name="value">The found property value.</param>
    /// <returns>True if the property was found, false otherwise.</returns>
    protected static bool TryGetNestedProperty(JsonElement element, string path, out JsonElement value)
    {
        value = default;
        var current = element;

        // Handle paths like "nested/prop" or just "prop"
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            if (!current.TryGetProperty(segment, out var next))
            {
                return false;
            }

            current = next;
        }

        value = current;
        return true;
    }
}
