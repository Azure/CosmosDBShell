// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.ComponentModel;
using System.Reflection;

using ModelContextProtocol.Server;

/// <summary>
/// Exposes MCP resources that provide contextual documentation to MCP clients.
/// </summary>
[McpServerResourceType]
internal class ResourceOperations
{
    private const string ScriptingUri = "cosmos://docs/scripting";
    private const string QueryLanguageUri = "cosmos://docs/nosql-query-language";

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
        var assembly = typeof(ResourceOperations).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new InvalidOperationException($"Embedded resource '{suffix}' not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Stream for embedded resource '{resourceName}' not found.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
