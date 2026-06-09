// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>
/// Shared helper for loading text content from embedded assembly resources used by the MCP layer.
/// </summary>
internal static class EmbeddedResourceLoader
{
    /// <summary>
    /// Loads the contents of the embedded resource whose manifest name ends with the given suffix.
    /// </summary>
    /// <param name="assembly">Assembly that contains the embedded resource.</param>
    /// <param name="suffix">Case-insensitive suffix of the embedded resource name (typically the file name).</param>
    /// <returns>The full text contents of the resource.</returns>
    public static string Load(Assembly assembly, string suffix)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource '{suffix}' not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Stream for embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
