// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Util;

internal static class WelcomeScreen
{
    private const string ResourceSuffix = "cosmos_welcome.ans";
    private static readonly Lazy<string> Content = new(Load);

    internal static string Text => Content.Value;

    internal static string Render(string template, string version, Func<string, string> getString)
    {
        return Regex.Replace(
            template,
            @"\{\{(VERSION|shell-welcome-[a-z-]+)\}\}",
            match => match.Groups[1].Value == "VERSION" ? version : getString(match.Groups[1].Value));
    }

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
        return Render(
            reader.ReadToEnd().TrimStart('\uFEFF'),
            ShellInterpreter.GetDisplayVersion(assembly),
            key => MessageService.GetString(key));
    }
}