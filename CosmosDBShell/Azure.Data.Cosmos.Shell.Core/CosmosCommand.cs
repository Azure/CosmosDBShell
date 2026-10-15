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
        var resolved = ResolveContainerReference(client, state, databaseOption, containerOption, commandName);

        // Validate that database and container exist
        await ValidateContainerExistsAsync(RequireConnectedState(state, commandName), resolved.DatabaseName, resolved.ContainerName, commandName, token);

        return resolved;
    }

    /// <summary>
    /// Resolves a container reference from command options and current shell state without
    /// performing an existence check. Commands that immediately read the container can use
    /// that read as validation and avoid redundant service requests.
    /// </summary>
    protected static (string DatabaseName, string ContainerName, Container Container) ResolveContainerReference(
        CosmosClient client,
        State state,
        string? databaseOption,
        string? containerOption,
        string commandName)
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

        var container = client.GetDatabase(databaseName).GetContainer(containerName);
        return (databaseName!, containerName!, container);
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
            _ => throw new JsonException("Partition key values must be JSON scalars."),
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
                throw new JsonException("Partition key components must be JSON scalars.");
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
            if (element.ValueKind == JsonValueKind.Object && !element.EnumerateObject().Any())
            {
                builder.AddNoneType();
            }
            else
            {
                AddPartitionKeyValue(builder, element);
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
                    if (root.GetArrayLength() is < 1 or > 3)
                    {
                        throw new JsonException("A hierarchical partition key must contain between one and three components.");
                    }

                    foreach (var component in root.EnumerateArray())
                    {
                        ValidatePartitionKeyArgumentComponent(component);
                    }

                    return CreatePartitionKey(root.EnumerateArray());
                }

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (!root.EnumerateObject().Any())
                    {
                        return PartitionKey.None;
                    }

                    throw new JsonException("Partition key cannot be a JSON object.");
                }

                ValidatePartitionKeyArgumentComponent(root);
                return CreatePartitionKey(root);
            }
        }

        return new PartitionKey(rawValue);
    }

    /// <summary>
    /// Creates a PartitionKey from a shell argument and reports invalid JSON with the
    /// command's localized error message.
    /// </summary>
    /// <param name="rawValue">The raw shell argument value.</param>
    /// <param name="commandName">The command reporting the error.</param>
    /// <param name="errorKey">The localization key for the invalid-key message.</param>
    /// <returns>A typed PartitionKey.</returns>
    protected static PartitionKey CreatePartitionKeyFromArgument(string rawValue, string commandName, string errorKey)
    {
        try
        {
            return CreatePartitionKeyFromArgument(rawValue);
        }
        catch (JsonException ex)
        {
            throw new CommandException(commandName, MessageService.GetString(errorKey), ex);
        }
    }

    internal static int GetPartitionKeyComponentCount(PartitionKey partitionKey)
    {
        using var document = ParsePartitionKeyJson(partitionKey);
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 1;
    }

    internal static JsonElement ToPartitionKeyJson(PartitionKey partitionKey)
    {
        using var document = ParsePartitionKeyJson(partitionKey);
        var root = document.RootElement;
        var value = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1 ? root[0] : root;

        // The SDK serializes numeric components as doubles (for example 7.0); render
        // integral values the way users supply them.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteNormalizedPartitionKeyValue(writer, value);
        }

        using var normalized = JsonDocument.Parse(buffer.ToArray());
        return normalized.RootElement.Clone();
    }

    // PartitionKey.None.ToString() is not JSON; represent it as one undefined component.
    private static JsonDocument ParsePartitionKeyJson(PartitionKey partitionKey) =>
        JsonDocument.Parse(partitionKey.Equals(PartitionKey.None) ? "[{}]" : partitionKey.ToString());

    private static void WriteNormalizedPartitionKeyValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var component in value.EnumerateArray())
                {
                    WriteNormalizedPartitionKeyValue(writer, component);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.Number when value.TryGetDecimal(out var number)
                && number == decimal.Truncate(number)
                && number >= long.MinValue
                && number <= long.MaxValue:
                writer.WriteNumberValue((long)number);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static void ValidatePartitionKeyArgumentComponent(JsonElement component)
    {
        if (component.ValueKind == JsonValueKind.Object && !component.EnumerateObject().Any())
        {
            return;
        }

        if (component.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)
            || (component.ValueKind == JsonValueKind.Number && !double.IsFinite(component.GetDouble())))
        {
            throw new JsonException("Partition key components must be JSON strings, finite numbers, booleans, null, or {}.");
        }
    }

    /// <summary>
    /// Creates a PartitionKey from an item using the container partition key definition.
    /// </summary>
    /// <param name="item">The item JSON.</param>
    /// <param name="partitionKeyPaths">The normalized partition key paths.</param>
    /// <param name="hasUndefinedComponent">Whether one or more configured paths are absent.</param>
    /// <param name="partitionKey">The resulting partition key.</param>
    /// <returns>True when the container defines at least one partition-key path.</returns>
    internal static bool TryCreatePartitionKeyFromItem(JsonElement item, IReadOnlyList<string> partitionKeyPaths, out bool hasUndefinedComponent, out PartitionKey partitionKey)
    {
        hasUndefinedComponent = false;
        if (partitionKeyPaths.Count == 0)
        {
            partitionKey = default;
            return false;
        }

        if (partitionKeyPaths.Count == 1)
        {
            if (!TryGetNestedProperty(item, partitionKeyPaths[0], out var component))
            {
                hasUndefinedComponent = true;
                partitionKey = PartitionKey.None;
                return true;
            }

            partitionKey = CreatePartitionKey(component);
            return true;
        }

        var builder = new PartitionKeyBuilder();
        foreach (var path in partitionKeyPaths)
        {
            if (!TryGetNestedProperty(item, path, out var keyComponent))
            {
                builder.AddNoneType();
                hasUndefinedComponent = true;
                continue;
            }

            AddPartitionKeyValue(builder, keyComponent);
        }

        partitionKey = builder.Build();
        return true;
    }

    protected static bool HasUndefinedPartitionKeyComponent(JsonElement item, IReadOnlyList<string> partitionKeyPaths)
    {
        return partitionKeyPaths.Any(path => !TryGetNestedProperty(item, path, out _));
    }

    protected static CommandWarning? AddUndefinedPartitionKeyWarning(
        CommandState commandState,
        int count,
        CommandWarning? warning = null)
    {
        if (count > 0)
        {
            var message = MessageService.GetArgsString("warning-undefined-partition-key-mutation", "count", count);
            if (warning is null)
            {
                return commandState.AddWarning(message);
            }

            warning.Message = message;
        }

        return warning;
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

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return false;
            }

            current = next;
        }

        value = current;
        return true;
    }
}
