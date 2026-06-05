// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using ModelContextProtocol.Server;

/// <summary>
/// Exposes MCP resources that provide contextual documentation and live shell state
/// to MCP clients. Documentation resources are static; shell-state resources read
/// from <see cref="ShellInterpreter.Instance"/> and reflect the user's current
/// connection, scope, command history, and command catalog.
/// </summary>
[McpServerResourceType]
internal class ResourceOperations
{
    private const string ScriptingUri = "cosmos://docs/scripting";
    private const string QueryLanguageUri = "cosmos://docs/nosql-query-language";
    private const string CommandsCatalogUri = "cosmos://docs/commands";
    private const string ConnectionUri = "cosmos://shell/connection";
    private const string LocationUri = "cosmos://shell/location";
    private const string HistoryUri = "cosmos://shell/history";
    private const string DatabasesUri = "cosmos://databases";
    private const string CurrentContainersUri = "cosmos://current/containers";
    private const string CurrentIndexingPolicyUri = "cosmos://current/container/indexing-policy";

    private const int HistoryItemLimit = 50;
    private const int DatabaseListLimit = 200;
    private const int ContainerListLimit = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    // Matches AccountKey=... in both quoted ("...") and unquoted forms so the
    // redactor catches quoted connection strings as well as bare key=value pairs.
    private static readonly Regex AccountKeyRedactor = new(
        @"AccountKey\s*=\s*(?:""[^""]*""|[^;""\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [McpServerResource(
        UriTemplate = ScriptingUri,
        Name = "cosmos-shell-scripting-guide",
        Title = "Writing Cosmos Shell Scripts",
        MimeType = "text/markdown")]
    [Description("Full reference for Cosmos Shell scripting: variables, control flow, functions, pipes, JSON paths, and practical examples. Use this resource when helping users write or debug .csh shell scripts.")]
    public static string GetScriptingGuide()
    {
        return LoadEmbeddedResource("programming.md");
    }

    [McpServerResource(
        UriTemplate = QueryLanguageUri,
        Name = "cosmos-nosql-query-language",
        Title = "Cosmos DB NoSQL Query Language Reference",
        MimeType = "text/markdown")]
    [Description("Complete reference for Azure Cosmos DB NoSQL query syntax: SELECT, FROM, WHERE, JOIN, aggregate functions, date/time functions, string functions, array functions, and best practices. Use this resource when helping users write or debug Cosmos DB queries.")]
    public static string GetQueryLanguageReference()
    {
        return LoadEmbeddedResource("nosql-query-language.md");
    }

    /// <summary>
    /// Returns a JSON catalog of all non-restricted shell commands, including
    /// descriptions, aliases, and examples. The list mirrors what the MCP
    /// <c>tools/list</c> endpoint advertises and is auto-generated from command metadata.
    /// </summary>
    [McpServerResource(
        UriTemplate = CommandsCatalogUri,
        Name = "cosmos-shell-commands",
        Title = "Cosmos Shell Command Catalog",
        MimeType = "application/json")]
    [Description("JSON catalog of all non-restricted Cosmos Shell commands with descriptions, MCP descriptions, aliases, and examples. Useful as a reference when the model needs to discover available commands without listing tools.")]
    public static string GetCommandsCatalog()
    {
        var catalog = new JsonArray();
        var commands = ShellInterpreter.Instance.App.Commands.Values
            .DistinctBy(c => c.CommandName)
            .Where(c => !c.McpRestricted)
            .OrderBy(c => c.CommandName, StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            var entry = new JsonObject
            {
                ["name"] = command.CommandName,
                ["description"] = command.Description ?? string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(command.McpDescription))
            {
                entry["mcpDescription"] = command.McpDescription;
            }

            if (command.Aliases.Count > 0)
            {
                var aliases = new JsonArray();
                foreach (var alias in command.Aliases)
                {
                    aliases.Add(alias);
                }

                entry["aliases"] = aliases;
            }

            var examples = new JsonArray();
            foreach (var (example, description) in command.ExamplesWithDescriptions)
            {
                examples.Add(new JsonObject
                {
                    ["example"] = example,
                    ["description"] = description ?? string.Empty,
                });
            }

            if (examples.Count > 0)
            {
                entry["examples"] = examples;
            }

            catalog.Add(entry);
        }

        return catalog.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Returns information about the current Cosmos DB connection: endpoint host,
    /// scope (database/container if any), and whether an ARM context is attached.
    /// </summary>
    [McpServerResource(
        UriTemplate = ConnectionUri,
        Name = "cosmos-shell-connection",
        Title = "Current Cosmos Shell Connection",
        MimeType = "application/json")]
    [Description("Current Cosmos Shell connection status: connected/disconnected, account endpoint host, current database, current container, and whether an ARM context is attached. Use this resource to understand the user's current scope without invoking the connect or pwd tools.")]
    public static string GetConnection()
    {
        var state = ShellInterpreter.Instance.State;
        var payload = new JsonObject
        {
            ["connected"] = state is ConnectedState,
            ["currentLocation"] = ShellLocation.GetCurrentLocation(state),
        };

        if (state is ConnectedState connectedState)
        {
            payload["endpoint"] = connectedState.Client.Endpoint.ToString();
            payload["endpointHost"] = connectedState.Client.Endpoint.Host;
            payload["hasArmContext"] = connectedState.ArmContext != null;
            payload["database"] = state is DatabaseState databaseState ? databaseState.DatabaseName : null;
            payload["container"] = state is ContainerState containerState ? containerState.ContainerName : null;
        }
        else
        {
            payload["endpoint"] = null;
            payload["endpointHost"] = null;
            payload["hasArmContext"] = false;
            payload["database"] = null;
            payload["container"] = null;
        }

        return payload.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Returns the current shell location as a simple JSON object. Mirrors what
    /// <c>pwd</c> would print and gets embedded in tool responses.
    /// </summary>
    [McpServerResource(
        UriTemplate = LocationUri,
        Name = "cosmos-shell-location",
        Title = "Current Cosmos Shell Location",
        MimeType = "application/json")]
    [Description("Current shell location as { \"location\": \"/db/container\" } or null when disconnected. Mirrors the pwd command output.")]
    public static string GetLocation()
    {
        var payload = new JsonObject
        {
            ["location"] = ShellLocation.GetCurrentLocation(ShellInterpreter.Instance.State),
        };
        return payload.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Returns the most recent shell commands the user has executed, with any
    /// embedded AccountKey values redacted to avoid leaking secrets.
    /// </summary>
    [McpServerResource(
        UriTemplate = HistoryUri,
        Name = "cosmos-shell-history",
        Title = "Recent Cosmos Shell Commands",
        MimeType = "application/json")]
    [Description("Most recent Cosmos Shell commands the user ran in the current session, with embedded connection-string AccountKey values redacted. Useful for grounding follow-up suggestions in what the user actually did.")]
    public static string GetHistory()
    {
        // Snapshot first: ShellInterpreter.History is a mutable List<string> that
        // PrintCommand appends to on the UI thread. Without this, a concurrent MCP
        // request can throw "Collection was modified" mid-enumeration.
        var snapshot = ShellInterpreter.Instance.History.ToArray();
        var recent = snapshot
            .Skip(Math.Max(0, snapshot.Length - HistoryItemLimit))
            .Select(SanitizeHistoryEntry);

        var entries = new JsonArray();
        foreach (var entry in recent)
        {
            entries.Add(entry);
        }

        return new JsonObject
        {
            ["count"] = entries.Count,
            ["entries"] = entries,
        }.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Lists the databases on the currently connected account, bounded to a
    /// safe number of entries.
    /// </summary>
    [McpServerResource(
        UriTemplate = DatabasesUri,
        Name = "cosmos-databases",
        Title = "Databases on Connected Account",
        MimeType = "application/json")]
    [Description("List of database names on the currently connected Cosmos DB account (read-only). Returns an error payload if the shell is not connected.")]
    public static async Task<string> GetDatabasesAsync(CancellationToken cancellationToken)
    {
        if (ShellInterpreter.Instance.State is not ConnectedState connectedState)
        {
            return NotConnectedError();
        }

        try
        {
            var ops = new DataPlaneCosmosResourceOperations(connectedState.Client);
            var names = new List<string>();
            var truncated = false;

            await foreach (var name in ops.GetDatabaseNamesAsync(cancellationToken))
            {
                if (names.Count >= DatabaseListLimit)
                {
                    truncated = true;
                    break;
                }

                names.Add(name);
            }

            var nameNodes = new JsonArray();
            foreach (var name in names)
            {
                nameNodes.Add(name);
            }

            return new JsonObject
            {
                ["count"] = names.Count,
                ["truncated"] = truncated,
                ["databases"] = nameNodes,
            }.ToJsonString(JsonOptions);
        }
        catch (CosmosException ex)
        {
            return ErrorPayload($"Cosmos error listing databases: {ex.StatusCode} {ex.Message}");
        }
    }

    /// <summary>
    /// Lists the containers in the current database scope.
    /// </summary>
    [McpServerResource(
        UriTemplate = CurrentContainersUri,
        Name = "cosmos-current-containers",
        Title = "Containers in Current Database",
        MimeType = "application/json")]
    [Description("List of container names in the database the shell is currently scoped to. Returns an error payload if the shell is not scoped to a database.")]
    public static async Task<string> GetCurrentContainersAsync(CancellationToken cancellationToken)
    {
        if (ShellInterpreter.Instance.State is not DatabaseState databaseState)
        {
            return ErrorPayload("Shell is not scoped to a database. Use the cd command to navigate into a database first.");
        }

        try
        {
            var ops = new DataPlaneCosmosResourceOperations(databaseState.Client);
            var names = new List<string>();
            var truncated = false;

            await foreach (var name in ops.GetContainerNamesAsync(databaseState.DatabaseName, cancellationToken))
            {
                if (names.Count >= ContainerListLimit)
                {
                    truncated = true;
                    break;
                }

                names.Add(name);
            }

            var nameNodes = new JsonArray();
            foreach (var name in names)
            {
                nameNodes.Add(name);
            }

            return new JsonObject
            {
                ["database"] = databaseState.DatabaseName,
                ["count"] = names.Count,
                ["truncated"] = truncated,
                ["containers"] = nameNodes,
            }.ToJsonString(JsonOptions);
        }
        catch (CosmosException ex)
        {
            return ErrorPayload($"Cosmos error listing containers: {ex.StatusCode} {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the current container's indexing policy as JSON.
    /// </summary>
    [McpServerResource(
        UriTemplate = CurrentIndexingPolicyUri,
        Name = "cosmos-current-indexing-policy",
        Title = "Current Container Indexing Policy",
        MimeType = "application/json")]
    [Description("Indexing policy of the container the shell is currently scoped to. Returns an error payload if the shell is not scoped to a container.")]
    public static async Task<string> GetCurrentIndexingPolicyAsync(CancellationToken cancellationToken)
    {
        if (ShellInterpreter.Instance.State is not ContainerState containerState)
        {
            return ErrorPayload("Shell is not scoped to a container. Use the cd command to navigate into a container first.");
        }

        try
        {
            var container = containerState.Client
                .GetDatabase(containerState.DatabaseName)
                .GetContainer(containerState.ContainerName);
            var response = await container.ReadContainerAsync(cancellationToken: cancellationToken);
            var properties = response.Resource;

            var payload = new JsonObject
            {
                ["database"] = containerState.DatabaseName,
                ["container"] = containerState.ContainerName,
            };

            if (properties?.IndexingPolicy != null)
            {
                var serialized = JsonSerializer.SerializeToNode(properties.IndexingPolicy);
                if (serialized != null)
                {
                    payload["indexingPolicy"] = serialized;
                }
            }

            return payload.ToJsonString(JsonOptions);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ErrorPayload($"Container '{containerState.ContainerName}' not found in database '{containerState.DatabaseName}'.");
        }
        catch (CosmosException ex)
        {
            return ErrorPayload($"Cosmos error reading indexing policy: {ex.StatusCode} {ex.Message}");
        }
    }

    internal static string SanitizeHistoryEntry(string entry)
    {
        return AccountKeyRedactor.Replace(entry, "AccountKey=***");
    }

    private static string LoadEmbeddedResource(string suffix)
    {
        return EmbeddedResourceLoader.Load(typeof(ResourceOperations).Assembly, suffix);
    }

    private static string NotConnectedError()
    {
        return ErrorPayload("Shell is not connected to a Cosmos DB account. Use the connect command first.");
    }

    private static string ErrorPayload(string message)
    {
        return new JsonObject
        {
            ["error"] = message,
            ["currentLocation"] = ShellLocation.GetCurrentLocation(ShellInterpreter.Instance.State),
        }.ToJsonString(JsonOptions);
    }
}
