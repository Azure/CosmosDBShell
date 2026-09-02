// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosDBShell.Localization;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fluent.Net;
using Fluent.Net.RuntimeAst;

internal static class FluentCatalogConverter
{
    private static readonly Regex PlaceholderRegex = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static SortedDictionary<string, string> Export(TextReader source)
    {
        var resource = Parse(source);
        var catalog = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, message) in resource.Entries)
        {
            ExportNode(message.Value, id, catalog);
            if (message.Attributes is null)
            {
                continue;
            }

            foreach (var (attributeName, attributeValue) in message.Attributes)
            {
                ExportNode(attributeValue, $"{id}.{attributeName}", catalog);
            }
        }

        return catalog;
    }

    public static string Import(TextReader source, IReadOnlyDictionary<string, string> catalog)
    {
        var resource = Parse(source);
        var expectedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, message) in resource.Entries)
        {
            message.Value = ImportNode(message.Value, id, catalog, expectedKeys);
            if (message.Attributes is null)
            {
                continue;
            }

            foreach (var attributeName in message.Attributes.Keys.ToArray())
            {
                message.Attributes[attributeName] = ImportNode(
                    message.Attributes[attributeName],
                    $"{id}.{attributeName}",
                    catalog,
                    expectedKeys);
            }
        }

        var unexpectedKeys = catalog.Keys.Where(key => !expectedKeys.Contains(key)).Order(StringComparer.Ordinal).ToArray();
        if (unexpectedKeys.Length > 0)
        {
            throw new InvalidDataException($"Translation catalog contains unexpected key(s): {string.Join(", ", unexpectedKeys)}");
        }

        return Serialize(resource);
    }

    public static void ExportFile(string sourcePath, string catalogPath)
    {
        using var source = File.OpenText(sourcePath);
        var catalog = Export(source);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!);
        File.WriteAllText(catalogPath, JsonSerializer.Serialize(catalog, JsonOptions) + Environment.NewLine);
    }

    public static void ImportFile(string sourcePath, string catalogPath, string outputPath)
    {
        using var source = File.OpenText(sourcePath);
        var catalog = ReadCatalog(catalogPath);
        var translatedResource = Import(source, catalog);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, translatedResource);
    }

    public static void VerifyFile(string sourcePath, string catalogPath)
    {
        using var source = File.OpenText(sourcePath);
        var expected = Export(source);
        var actual = ReadCatalog(catalogPath);
        if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var value) || value != pair.Value))
        {
            throw new InvalidDataException($"'{catalogPath}' is not synchronized with '{sourcePath}'. Run the localization export command.");
        }
    }

    private static SortedDictionary<string, string> ReadCatalog(string path)
    {
        var catalog = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(path));
        return catalog ?? throw new InvalidDataException($"Translation catalog '{path}' is empty or invalid.");
    }

    private static FluentResource Parse(TextReader source)
    {
        var resource = FluentResource.FromReader(source);
        if (resource.Errors.Count > 0)
        {
            throw new InvalidDataException(
                $"Fluent resource contains invalid syntax:{Environment.NewLine}{string.Join(Environment.NewLine, resource.Errors)}");
        }

        return resource;
    }

    private static void ExportNode(Node node, string path, IDictionary<string, string> catalog)
    {
        switch (node)
        {
            case StringLiteral text:
                catalog.Add(path, text.Value);
                break;
            case Pattern pattern:
                catalog.Add(path, ExportPattern(pattern));
                ExportNestedPatterns(pattern, path, catalog);
                break;
            default:
                throw new InvalidDataException($"Unsupported translatable Fluent node '{node.GetType().Name}' at '{path}'.");
        }
    }

    private static string ExportPattern(Pattern pattern)
    {
        var builder = new StringBuilder();
        var placeholderIndex = 0;
        foreach (var element in pattern.Elements)
        {
            if (element is StringLiteral text)
            {
                builder.Append(text.Value);
            }
            else
            {
                builder.Append('{').Append(placeholderIndex).Append('}');
                placeholderIndex++;
            }
        }

        return builder.ToString();
    }

    private static void ExportNestedPatterns(Pattern pattern, string path, IDictionary<string, string> catalog)
    {
        var placeholderIndex = 0;
        foreach (var element in pattern.Elements)
        {
            if (element is StringLiteral)
            {
                continue;
            }

            if (element is SelectExpression select)
            {
                foreach (var variant in select.Variants)
                {
                    ExportNode(variant.Value, $"{path}.__p{placeholderIndex}.{GetVariantKey(variant.Key)}", catalog);
                }
            }

            placeholderIndex++;
        }
    }

    private static Node ImportNode(
        Node node,
        string path,
        IReadOnlyDictionary<string, string> catalog,
        ISet<string> expectedKeys)
    {
        expectedKeys.Add(path);
        if (!catalog.TryGetValue(path, out var translation))
        {
            throw new InvalidDataException($"Translation catalog is missing key '{path}'.");
        }

        switch (node)
        {
            case StringLiteral:
                return new StringLiteral { Value = translation };
            case Pattern pattern:
                ImportNestedPatterns(pattern, path, catalog, expectedKeys);
                pattern.Elements = ImportPattern(path, pattern, translation);
                return pattern;
            default:
                throw new InvalidDataException($"Unsupported translatable Fluent node '{node.GetType().Name}' at '{path}'.");
        }
    }

    private static void ImportNestedPatterns(
        Pattern pattern,
        string path,
        IReadOnlyDictionary<string, string> catalog,
        ISet<string> expectedKeys)
    {
        var placeholderIndex = 0;
        foreach (var element in pattern.Elements)
        {
            if (element is StringLiteral)
            {
                continue;
            }

            if (element is SelectExpression select)
            {
                foreach (var variant in select.Variants)
                {
                    variant.Value = ImportNode(
                        variant.Value,
                        $"{path}.__p{placeholderIndex}.{GetVariantKey(variant.Key)}",
                        catalog,
                        expectedKeys);
                }
            }

            placeholderIndex++;
        }
    }

    private static ICollection<Node> ImportPattern(string path, Pattern source, string translation)
    {
        var placeholders = source.Elements.Where(element => element is not StringLiteral).ToArray();
        var seen = new HashSet<int>();
        var elements = new List<Node>();
        var position = 0;
        foreach (Match match in PlaceholderRegex.Matches(translation))
        {
            AddText(elements, translation[position..match.Index]);
            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (index >= placeholders.Length)
            {
                throw new InvalidDataException($"Translation '{path}' contains unknown placeholder '{{{index}}}'.");
            }

            if (!seen.Add(index))
            {
                throw new InvalidDataException($"Translation '{path}' contains duplicate placeholder '{{{index}}}'.");
            }

            elements.Add(placeholders[index]);
            position = match.Index + match.Length;
        }

        AddText(elements, translation[position..]);
        var missing = Enumerable.Range(0, placeholders.Length).Where(index => !seen.Contains(index)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"Translation '{path}' is missing placeholder(s): {string.Join(", ", missing.Select(index => $"{{{index}}}"))}");
        }

        return elements;
    }

    private static void AddText(ICollection<Node> elements, string value)
    {
        if (value.Length > 0)
        {
            elements.Add(new StringLiteral { Value = value });
        }
    }

    private static string GetVariantKey(Node key)
    {
        return key switch
        {
            VariantName name => name.Name,
            NumberLiteral number => number.Value,
            _ => throw new InvalidDataException($"Unsupported Fluent variant key '{key.GetType().Name}'."),
        };
    }

    private static string Serialize(FluentResource resource)
    {
        var builder = new StringBuilder();
        foreach (var (id, message) in resource.Entries)
        {
            WriteEntry(builder, id, message.Value, isAttribute: false);
            if (message.Attributes is not null)
            {
                foreach (var (attributeName, attributeValue) in message.Attributes)
                {
                    WriteEntry(builder, attributeName, attributeValue, isAttribute: true);
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void WriteEntry(StringBuilder builder, string id, Node value, bool isAttribute)
    {
        builder.Append(isAttribute ? "    ." : string.Empty).Append(id).Append(" = ");
        builder.AppendLine(RenderPatternValue(value));
    }

    private static string RenderPatternValue(Node node)
    {
        return node switch
        {
            StringLiteral text => RenderText(text.Value),
            Pattern pattern => string.Concat(pattern.Elements.Select(RenderPatternElement)),
            _ => throw new InvalidDataException($"Unsupported Fluent value node '{node.GetType().Name}'."),
        };
    }

    private static string RenderPatternElement(Node node)
    {
        return node switch
        {
            StringLiteral text => RenderText(text.Value),
            VariableReference variable => $"{{ ${variable.Name} }}",
            MessageReference message => $"{{ {message.Name} }}",
            SelectExpression select => RenderSelectExpression(select),
            _ => throw new InvalidDataException($"Unsupported Fluent pattern element '{node.GetType().Name}'."),
        };
    }

    private static string RenderSelectExpression(SelectExpression select)
    {
        var builder = new StringBuilder();
        builder.Append("{ ").Append(RenderExpression(select.Expression)).Append(" ->").AppendLine();
        for (var index = 0; index < select.Variants.Count; index++)
        {
            var variant = select.Variants[index];
            builder.Append(index == select.DefaultIndex ? "    *[" : "    [")
                .Append(GetVariantKey(variant.Key))
                .Append("] ")
                .AppendLine(RenderPatternValue(variant.Value));
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static string RenderText(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '\n':
                    builder.AppendLine();
                    if (index + 1 < value.Length && value[index + 1] != '\n')
                    {
                        builder.Append("    ");
                        var spaces = 0;
                        while (index + 1 < value.Length && value[index + 1] == ' ')
                        {
                            spaces++;
                            index++;
                        }

                        if (spaces > 0)
                        {
                            builder.Append("{ \"").Append(' ', spaces).Append("\" }");
                        }
                    }

                    break;
                case '{':
                    builder.Append("{ \"{\" }");
                    break;
                case '}':
                    builder.Append("{ \"}\" }");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string RenderExpression(Node node)
    {
        return node switch
        {
            VariableReference variable => $"${variable.Name}",
            MessageReference message => message.Name,
            NumberLiteral number => number.Value,
            StringLiteral text => $"\"{text.Value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            _ => throw new InvalidDataException($"Unsupported Fluent expression '{node.GetType().Name}'."),
        };
    }

}
