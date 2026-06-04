//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("index")]
[CosmosExample("index show", Description = "Display the current container's indexing policy")]
[CosmosExample("index add /address/*", Description = "Add a path to the included paths of the indexing policy")]
[CosmosExample("index remove /address/*", Description = "Remove a path from the indexing policy")]
[CosmosExample("index set --mode=consistent --automatic=true", Description = "Update the indexing mode and automatic flag")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Index",
    Description = @"
Manages the indexing policy of a Cosmos DB container through subcommands:
- 'show' reads and returns the current indexing policy as JSON.
- 'add <path...>' adds one or more paths to the included paths.
- 'remove <path...>' removes one or more paths from the included and excluded paths.
- 'set' updates the indexing policy. Pass --mode and/or --automatic to patch the current policy, or provide a full indexing policy JSON document to replace it.

Paths use the Cosmos DB indexing path syntax, for example '/address/*' or '/name/?'.
",
    ReadOnly = false)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class IndexCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    [CosmosParameter("subcommand", RequiredErrorKey = "command-index-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("paths", IsRequired = false)]
    public string[]? Paths { get; init; }

    [CosmosOption("mode", "m")]
    public string? Mode { get; init; }

    [CosmosOption("automatic", "a")]
    public string? Automatic { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token) =>
        shell.State.AcceptAsync(this, shell, token);

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("index");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database) && !string.IsNullOrEmpty(this.Container))
        {
            return await this.ExecuteOnContainerAsync(state, this.Database, this.Container, token);
        }

        throw new NotInContainerException("index");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;

        if (!string.IsNullOrEmpty(this.Container))
        {
            return await this.ExecuteOnContainerAsync(state, databaseName, this.Container, token);
        }

        throw new NotInContainerException("index");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.ExecuteOnContainerAsync(state, databaseName, containerName, token);
    }

    /// <summary>
    /// Adds the given paths to the included paths of an indexing policy. Paths that
    /// are already present are left untouched, and any matching excluded path is
    /// removed so the newly included path is actually indexed.
    /// </summary>
    internal static string AddIncludedPaths(string policyJson, IReadOnlyList<string> paths)
    {
        var root = ParseObject(policyJson);
        var included = GetOrCreateArray(root, "includedPaths");
        var excluded = FindArray(root, "excludedPaths");

        foreach (var path in paths)
        {
            if (!ContainsPath(included, path))
            {
                included.Add(new JsonObject { ["path"] = path });
            }

            RemovePath(excluded, path);
        }

        return Serialize(root);
    }

    /// <summary>
    /// Removes the given paths from both the included and excluded paths of an
    /// indexing policy.
    /// </summary>
    internal static string RemovePaths(string policyJson, IReadOnlyList<string> paths)
    {
        var root = ParseObject(policyJson);
        var included = FindArray(root, "includedPaths");
        var excluded = FindArray(root, "excludedPaths");

        foreach (var path in paths)
        {
            RemovePath(included, path);
            RemovePath(excluded, path);
        }

        return Serialize(root);
    }

    /// <summary>
    /// Applies indexing mode and automatic flag changes onto an existing policy.
    /// Properties that are not supplied are left unchanged.
    /// </summary>
    internal static string ApplySettings(string policyJson, string? mode, bool? automatic)
    {
        var root = ParseObject(policyJson);

        if (!string.IsNullOrEmpty(mode))
        {
            SetProperty(root, "indexingMode", mode);
        }

        if (automatic.HasValue)
        {
            SetProperty(root, "automatic", automatic.Value);
        }

        return Serialize(root);
    }

    /// <summary>
    /// Parses the value of the <c>--automatic</c> option. Returns null when no value
    /// was supplied.
    /// </summary>
    internal static bool? ParseAutomatic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value.Trim(), out var parsed))
        {
            return parsed;
        }

        throw new CommandException("index", MessageService.GetString("command-index-error-invalid_automatic"));
    }

    private static JsonObject ParseObject(string policyJson)
    {
        try
        {
            return JsonNode.Parse(policyJson) as JsonObject
                ?? throw new CommandException("index", MessageService.GetString("command-index-error-invalid_policy"));
        }
        catch (JsonException ex)
        {
            throw new CommandException("index", MessageService.GetString("command-index-error-invalid_policy"), ex);
        }
    }

    private static JsonArray? FindArray(JsonObject obj, string name)
    {
        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && pair.Value is JsonArray array)
            {
                return array;
            }
        }

        return null;
    }

    private static JsonArray GetOrCreateArray(JsonObject obj, string name)
    {
        var existing = FindArray(obj, name);
        if (existing is not null)
        {
            return existing;
        }

        var array = new JsonArray();
        obj[name] = array;
        return array;
    }

    private static void SetProperty(JsonObject obj, string camelCaseName, JsonNode? value)
    {
        string? existingKey = null;
        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, camelCaseName, StringComparison.OrdinalIgnoreCase))
            {
                existingKey = pair.Key;
                break;
            }
        }

        obj[existingKey ?? camelCaseName] = value;
    }

    private static bool ContainsPath(JsonArray array, string path)
    {
        foreach (var node in array)
        {
            if (string.Equals(GetPathValue(node), path, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemovePath(JsonArray? array, string path)
    {
        if (array is null)
        {
            return;
        }

        for (int i = array.Count - 1; i >= 0; i--)
        {
            if (string.Equals(GetPathValue(array[i]), path, StringComparison.Ordinal))
            {
                array.RemoveAt(i);
            }
        }
    }

    private static string? GetPathValue(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (string.Equals(pair.Key, "path", StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value?.GetValue<string>();
                }
            }
        }

        return null;
    }

    private static string Serialize(JsonObject root)
    {
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static CommandState BuildResult(string json)
    {
        using var jsonDoc = JsonDocument.Parse(json);
        return new CommandState
        {
            Result = new ShellJson(jsonDoc.RootElement.Clone()),
        };
    }

    private async Task<CommandState> ExecuteOnContainerAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        await ValidateContainerExistsAsync(state, databaseName, containerName, "index", token);

        switch (this.Subcommand.Trim().ToLowerInvariant())
        {
            case "show":
                return await this.ShowAsync(state, databaseName, containerName, token);
            case "add":
                return await this.AddAsync(state, databaseName, containerName, token);
            case "remove":
                return await this.RemoveAsync(state, databaseName, containerName, token);
            case "set":
                return await this.SetAsync(state, databaseName, containerName, token);
            default:
                throw new CommandException(
                    "index",
                    MessageService.GetArgsString("command-index-error-invalid_subcommand", "subcommand", this.Subcommand));
        }
    }

    private async Task<CommandState> ShowAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        string json = await this.ReadPolicyAsync(state, databaseName, containerName, token);

        return BuildResult(json);
    }

    private async Task<CommandState> AddAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        var paths = this.RequirePaths();
        string current = await this.ReadPolicyAsync(state, databaseName, containerName, token);
        string updated = AddIncludedPaths(current, paths);
        return await this.WritePolicyAsync(state, databaseName, containerName, updated, token);
    }

    private async Task<CommandState> RemoveAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        var paths = this.RequirePaths();
        string current = await this.ReadPolicyAsync(state, databaseName, containerName, token);
        string updated = RemovePaths(current, paths);
        return await this.WritePolicyAsync(state, databaseName, containerName, updated, token);
    }

    private async Task<CommandState> SetAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        bool? automatic = ParseAutomatic(this.Automatic);
        string? policyArgument = this.PolicyArgument();

        if (policyArgument is null && string.IsNullOrEmpty(this.Mode) && !automatic.HasValue)
        {
            throw new CommandException("index", MessageService.GetString("command-index-error-missing_set_args"));
        }

        string baseJson = policyArgument ?? await this.ReadPolicyAsync(state, databaseName, containerName, token);
        string updated = ApplySettings(baseJson, this.Mode, automatic);
        return await this.WritePolicyAsync(state, databaseName, containerName, updated, token);
    }

    private string[] RequirePaths()
    {
        if (this.Paths is null || this.Paths.Length == 0)
        {
            throw new CommandException("index", MessageService.GetString("command-index-error-missing_paths"));
        }

        return this.Paths;
    }

    private string? PolicyArgument()
    {
        if (this.Paths is null || this.Paths.Length == 0)
        {
            return null;
        }

        string candidate = string.Join(" ", this.Paths).Trim();
        return candidate.StartsWith('{') ? candidate : null;
    }

    private async Task<string> ReadPolicyAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        try
        {
            return await CosmosResourceFacade.GetIndexingPolicyJsonAsync(state, databaseName, containerName, token);
        }
        catch (IndexPolicyMissingException ex)
        {
            throw new CommandException("index", MessageService.GetString("command-index-error-no_policy"), ex);
        }
    }

    private async Task<CommandState> WritePolicyAsync(ConnectedState state, string databaseName, string containerName, string policyJson, CancellationToken token)
    {
        string updatedJson;
        try
        {
            updatedJson = await CosmosResourceFacade.ReplaceIndexingPolicyAsync(state, databaseName, containerName, policyJson, token);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new CommandException("index", MessageService.GetString("command-index-error-invalid_policy"), ex);
        }

        ShellInterpreter.WriteLine(MessageService.GetString("command-index-updated"));
        return BuildResult(updatedJson);
    }
}
