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
