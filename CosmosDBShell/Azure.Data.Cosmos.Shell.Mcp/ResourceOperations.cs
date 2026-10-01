// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

using ModelContextProtocol.Server;

/// <summary>
/// Exposes MCP resources that provide contextual documentation to MCP clients.
/// </summary>
[McpServerResourceType]
internal class ResourceOperations
{
    internal const string CurrentLocationUri = "cosmos://shell/current-location";
    private const string ScriptingUri = "cosmos://docs/scripting";
    private const string QueryLanguageUri = "cosmos://docs/nosql-query-language";

    [McpServerResource(
        UriTemplate = CurrentLocationUri,
        Name = "cosmos-shell-current-location",
        Title = "Current Cosmos Shell Location",
        MimeType = "application/json")]
    [Description("Current shared shell navigation location and account endpoint. Subscribe to this resource for changes made in the interactive shell or by MCP clients. Read it again after an update notification.")]
    public static string GetCurrentLocation()
    {
        return GetCurrentLocation(ShellInterpreter.Instance.State);
    }

    internal static string GetCurrentLocation(State state)
    {
        return JsonSerializer.Serialize(new
        {
            currentLocation = ShellLocation.GetCurrentLocation(state),
            currentAccountEndpoint = state is ConnectedState connected ? connected.Client.Endpoint.ToString() : null,
        });
    }

    /// <summary>
    /// Returns the Cosmos Shell scripting / programming guide so the LLM can
    /// help users author .csh scripts.
    /// </summary>
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

    /// <summary>
    /// Returns the Cosmos DB NoSQL query language reference so the LLM can
    /// help users write correct queries.
    /// </summary>
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

    private static string LoadEmbeddedResource(string suffix)
    {
        return EmbeddedResourceLoader.Load(typeof(ResourceOperations).Assembly, suffix);
    }
}
