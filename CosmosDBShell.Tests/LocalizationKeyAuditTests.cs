// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Reflection;
using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class LocalizationKeyAuditTests
{
    [Fact]
    public void ReferencedLocalizationKeys_AreDefinedInEnglishResource()
    {
        var root = FindRepositoryRoot();
        var definedKeys = LoadDefinedKeys(Path.Combine(root, "CosmosDBShell", "lang", "en.ftl"));
        var referencedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in ExtractLiteralMessageKeys(Path.Combine(root, "CosmosDBShell")))
        {
            referencedKeys.Add(key);
        }

        foreach (var command in new CommandRunner().Commands.Values)
        {
            referencedKeys.Add($"command-{command.CommandName}-description");

            foreach (var parameter in command.Parameters)
            {
                referencedKeys.Add($"command-{command.CommandName}-description-{parameter.Name[0]}");
            }

            foreach (var option in command.Options)
            {
                referencedKeys.Add($"command-{command.CommandName}-description-{option.Name[0]}");
            }
        }

        foreach (var key in ExtractStatementHelpKeys())
        {
            referencedKeys.Add(key + "-description");
            referencedKeys.Add(key + "-syntax");
            referencedKeys.Add(key + "-example");
        }

        var missing = referencedKeys.Where(key => !definedKeys.Contains(key)).OrderBy(key => key).ToArray();
        Assert.True(missing.Length == 0, "Missing localization keys: " + string.Join(", ", missing));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CosmosDBShell.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static HashSet<string> LoadDefinedKeys(string ftlPath)
    {
        var content = File.ReadAllText(ftlPath);
        var matches = Regex.Matches(content, "(?m)^([A-Za-z0-9_.-]+)\\s*=");
        return matches.Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> ExtractLiteralMessageKeys(string sourceRoot)
    {
        var pattern = new Regex("MessageService\\.Get(?:String|ArgsString)\\(\"([^\"]+)\"", RegexOptions.Compiled);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) || p.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            foreach (Match match in pattern.Matches(content))
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static IEnumerable<string> ExtractStatementHelpKeys()
    {
        var statementType = typeof(Statement);

        foreach (var type in statementType.Assembly.GetTypes())
        {
            if (!statementType.IsAssignableFrom(type) || type.IsAbstract)
            {
                continue;
            }

            var attribute = type.GetCustomAttributes(inherit: false)
                .FirstOrDefault(candidate => candidate.GetType().Name == "AstHelpAttribute");

            if (attribute == null)
            {
                continue;
            }

            var keyProperty = attribute.GetType().GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
            var key = keyProperty?.GetValue(attribute) as string;
            if (!string.IsNullOrEmpty(key))
            {
                yield return key;
            }
        }
    }
}