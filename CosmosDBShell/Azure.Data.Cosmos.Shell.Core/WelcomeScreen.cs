// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal static class WelcomeScreen
{
    private const string ResourceSuffix = "cosmos_welcome.ans";
    private static readonly Lazy<string> Content = new(Load);

    internal static string Text => Content.Value;

    internal static void WriteTo(TextWriter writer)
    {
        writer.Write(Content.Value);
        writer.Write("\u001b[0m");
        if (!Content.Value.EndsWith('\n'))
        {
            writer.WriteLine();
        }
    }

    private static string Load()
    {
        var assembly = typeof(WelcomeScreen).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceSuffix}' not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Stream for embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().TrimStart('\uFEFF');
    }
}