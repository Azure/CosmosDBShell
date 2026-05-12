// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System.IO;
using System.Linq;
using Azure.Data.Cosmos.Shell.Util;
using Tomlyn;
using Tomlyn.Model;

/// <summary>
/// Reads and writes <see cref="ThemeOptions"/> as TOML files.
/// </summary>
/// <remarks>
/// <para>File schema (all sections optional):</para>
/// <code>
/// name        = "solarized-light"
/// description = "Solarized-style light palette"
/// extends     = "default"
///
/// [colors]
/// command           = "yellow"
/// literal           = "purple"
/// bracket_cycle     = ["yellow", "fuchsia", "aqua"]
///
/// [styles]
/// help_header     = "bold"
/// unknown_command = "bold red"
/// </code>
/// <para>Snake-case keys map to <see cref="ThemeOptions"/> properties via the
/// <see cref="ColorSlots"/> and <see cref="StyleSlots"/> dictionaries below.
/// Unknown keys produce a load-time warning but do not fail the load.</para>
/// </remarks>
internal static class ThemeFile
{
    /// <summary>The maximum chain length when resolving <c>extends</c>; protects against cycles.</summary>
    private const int MaxExtendsDepth = 8;

    /// <summary>
    /// Slot definitions for the <c>[colors]</c> section. Each entry maps a snake-case
    /// TOML key to a getter and a "copy with new value" function on
    /// <see cref="ThemeOptions"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (Func<ThemeOptions, string> Get, Func<ThemeOptions, string, ThemeOptions> With)> ColorSlots
        = new Dictionary<string, (Func<ThemeOptions, string>, Func<ThemeOptions, string, ThemeOptions>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["command"] = (o => o.CommandColor, (o, v) => o with { CommandColor = v }),
            ["argument_name"] = (o => o.ArgumentNameColor, (o, v) => o with { ArgumentNameColor = v }),
            ["connected_prompt"] = (o => o.ConnectedPromptColor, (o, v) => o with { ConnectedPromptColor = v }),
            ["database_name"] = (o => o.DatabaseNameColor, (o, v) => o with { DatabaseNameColor = v }),
            ["container_name"] = (o => o.ContainerNameColor, (o, v) => o with { ContainerNameColor = v }),
            ["redirection"] = (o => o.RedirectionColor, (o, v) => o with { RedirectionColor = v }),
            ["json_property"] = (o => o.JsonPropertyColor, (o, v) => o with { JsonPropertyColor = v }),
            ["json_punctuation"] = (o => o.JsonPunctuationColor, (o, v) => o with { JsonPunctuationColor = v }),
            ["literal"] = (o => o.LiteralColor, (o, v) => o with { LiteralColor = v }),
            ["keyword"] = (o => o.KeywordColor, (o, v) => o with { KeywordColor = v }),
            ["error"] = (o => o.ErrorColor, (o, v) => o with { ErrorColor = v }),
            ["operator"] = (o => o.OperatorColor, (o, v) => o with { OperatorColor = v }),
            ["table_value"] = (o => o.TableValueColor, (o, v) => o with { TableValueColor = v }),
            ["warning"] = (o => o.WarningColor, (o, v) => o with { WarningColor = v }),
            ["directory"] = (o => o.DirectoryColor, (o, v) => o with { DirectoryColor = v }),
            ["muted"] = (o => o.MutedColor, (o, v) => o with { MutedColor = v }),
            ["help_accent"] = (o => o.HelpAccentColor, (o, v) => o with { HelpAccentColor = v }),
            ["help_placeholder"] = (o => o.HelpPlaceholderColor, (o, v) => o with { HelpPlaceholderColor = v }),
            ["help_variable"] = (o => o.HelpVariableColor, (o, v) => o with { HelpVariableColor = v }),
        };

    /// <summary>Slot definitions for the <c>[styles]</c> section.</summary>
    private static readonly IReadOnlyDictionary<string, (Func<ThemeOptions, string> Get, Func<ThemeOptions, string, ThemeOptions> With)> StyleSlots
        = new Dictionary<string, (Func<ThemeOptions, string>, Func<ThemeOptions, string, ThemeOptions>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["help_header"] = (o => o.HelpHeaderStyle, (o, v) => o with { HelpHeaderStyle = v }),
            ["help_name"] = (o => o.HelpNameStyle, (o, v) => o with { HelpNameStyle = v }),
            ["unknown_command"] = (o => o.UnknownCommandStyle, (o, v) => o with { UnknownCommandStyle = v }),
        };

    /// <summary>
    /// Returns the maximum chain length used when resolving <c>extends</c>.
    /// Exposed so the registry can enforce the same limit while resolving.
    /// </summary>
    public static int MaxResolveDepth => MaxExtendsDepth;

    /// <summary>
    /// Loads a single TOML file into a <see cref="ThemeOptions"/>. The <c>extends</c>
    /// chain (if any) is resolved by looking up base profiles in <paramref name="baseLookup"/>;
    /// supply <see cref="ThemeProfiles.TryGet"/> for the built-ins-only case.
    /// </summary>
    /// <param name="path">Absolute path to the TOML file.</param>
    /// <param name="baseLookup">Resolves a base profile name to a <see cref="ThemeOptions"/>.</param>
    /// <returns>
    /// A populated <see cref="ThemeFileResult"/> with the resolved theme and any non-fatal
    /// warnings collected while loading.
    /// </returns>
    /// <exception cref="ThemeLoadException">Thrown for malformed TOML, unknown <c>extends</c>, cycles, or invalid color values.</exception>
    public static ThemeFileResult Load(string path, Func<string, ThemeOptions?> baseLookup)
    {
        var fullPath = Path.GetFullPath(path);
        var text = File.ReadAllText(fullPath);
        return Parse(text, fullPath, baseLookup);
    }

    /// <summary>
    /// Parses TOML text into a <see cref="ThemeOptions"/>. Same contract as
    /// <see cref="Load"/>, but accepts the text directly so tests can avoid touching
    /// the file system.
    /// </summary>
    public static ThemeFileResult Parse(string text, string sourceLabel, Func<string, ThemeOptions?> baseLookup)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        if (!Toml.TryToModel(text, out TomlTable? model, out var diagnostics))
        {
            throw new ThemeLoadException(MessageService.GetArgsString(
                "theme-file-error-parse",
                "source",
                sourceLabel,
                "details",
                string.Join("; ", diagnostics.Select(d => d.Message))));
        }

        var name = ReadString(model, "name") ?? Path.GetFileNameWithoutExtension(sourceLabel);
        var description = ReadString(model, "description");
        var extends = ReadString(model, "extends") ?? "default";

        // Detect the trivial self-cycle here; deeper cycles are handled by ThemeRegistry.
        if (string.Equals(extends, name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ThemeLoadException(MessageService.GetArgsString(
                "theme-file-error-extends-self",
                "source",
                sourceLabel,
                "name",
                name));
        }

        var baseOptions = baseLookup(extends)
            ?? throw new ThemeLoadException(MessageService.GetArgsString(
                "theme-file-error-extends-unknown",
                "source",
                sourceLabel,
                "name",
                extends));

        var built = baseOptions;
        built = ApplyTable(built, model, "colors", ColorSlots, sourceLabel, warnings, errors, TryValidateColorValue);
        built = ApplyTable(built, model, "styles", StyleSlots, sourceLabel, warnings, errors, TryValidateStyleValue);

        if (model.TryGetValue("colors", out var colorsObj) && colorsObj is TomlTable colorsTable && colorsTable.TryGetValue("bracket_cycle", out var cycleObj))
        {
            if (cycleObj is TomlArray array)
            {
                var cycle = array.Select(item => item?.ToString() ?? string.Empty).ToArray();
                if (cycle.Length == 0)
                {
                    errors.Add(MessageService.GetArgsString(
                        "theme-file-error-empty-bracket-cycle",
                        "source",
                        sourceLabel));
                }
                else
                {
                    var anyInvalid = false;
                    foreach (var color in cycle)
                    {
                        if (!TryValidateColorValue("bracket_cycle", color, sourceLabel, errors))
                        {
                            anyInvalid = true;
                        }
                    }

                    if (!anyInvalid)
                    {
                        built = built with { BracketCycle = cycle };

                        if (cycle.Length == 1)
                        {
                            warnings.Add(MessageService.GetArgsString(
                                "theme-file-warning-bracket-cycle-single",
                                "source",
                                sourceLabel));
                        }

                        if (cycle.Distinct(StringComparer.OrdinalIgnoreCase).Count() < cycle.Length)
                        {
                            warnings.Add(MessageService.GetArgsString(
                                "theme-file-warning-bracket-cycle-duplicates",
                                "source",
                                sourceLabel));
                        }
                    }
                }
            }
            else
            {
                warnings.Add(MessageService.GetArgsString(
                    "theme-file-warning-bracket-cycle-not-array",
                    "source",
                    sourceLabel));
            }
        }

        if (errors.Count > 0)
        {
            throw new ThemeLoadException(string.Join(Environment.NewLine, errors));
        }

        return new ThemeFileResult(name, description, extends, built, sourceLabel, warnings);
    }

    /// <summary>
    /// Writes <paramref name="options"/> to <paramref name="path"/> as a self-contained
    /// TOML file. Every slot is written explicitly; no <c>extends</c> line is emitted,
    /// so the file does not depend on any other profile being present at load time.
    /// </summary>
    public static void Save(string name, ThemeOptions options, string path, string? description = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("name        = ").AppendLine(QuoteString(name));
        if (!string.IsNullOrEmpty(description))
        {
            sb.Append("description = ").AppendLine(QuoteString(description));
        }

        sb.AppendLine();

        sb.AppendLine("[colors]");
        foreach (var (key, accessors) in ColorSlots.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(key).Append(" = ").AppendLine(QuoteString(accessors.Get(options)));
        }

        sb.Append("bracket_cycle = [");
        for (var i = 0; i < options.BracketCycle.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(QuoteString(options.BracketCycle[i]));
        }

        sb.AppendLine("]");
        sb.AppendLine();

        sb.AppendLine("[styles]");
        foreach (var (key, accessors) in StyleSlots.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(key).Append(" = ").AppendLine(QuoteString(accessors.Get(options)));
        }

        sb.AppendLine();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, sb.ToString());
    }

    /// <summary>
    /// The default user-themes directory: <c>~/.cosmosdbshell/themes</c>.
    /// </summary>
    public static string DefaultUserThemesDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cosmosdbshell", "themes");
    }

    /// <summary>
    /// Returns the set of TOML files in <paramref name="directory"/>, or an empty
    /// array if the directory does not exist.
    /// </summary>
    public static string[] EnumerateThemeFiles(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.toml", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
    }

    private static ThemeOptions ApplyTable(
        ThemeOptions accumulator,
        TomlTable model,
        string sectionName,
        IReadOnlyDictionary<string, (Func<ThemeOptions, string> Get, Func<ThemeOptions, string, ThemeOptions> With)> slots,
        string sourceLabel,
        List<string> warnings,
        List<string> errors,
        Func<string, string, string, List<string>, bool> tryValidateValue)
    {
        if (!model.TryGetValue(sectionName, out var sectionObj) || sectionObj is not TomlTable section)
        {
            return accumulator;
        }

        foreach (var entry in section)
        {
            if (string.Equals(entry.Key, "bracket_cycle", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!slots.TryGetValue(entry.Key, out var slot))
            {
                warnings.Add(MessageService.GetArgsString(
                    "theme-file-warning-unknown-key",
                    "source",
                    sourceLabel,
                    "section",
                    sectionName,
                    "key",
                    entry.Key));
                continue;
            }

            var value = entry.Value as string ?? entry.Value?.ToString() ?? string.Empty;
            if (!tryValidateValue(entry.Key, value, sourceLabel, errors))
            {
                continue;
            }

            accumulator = slot.With(accumulator, value);
        }

        return accumulator;
    }

    private static string? ReadString(TomlTable model, string key)
    {
        return model.TryGetValue(key, out var value) ? value as string ?? value?.ToString() : null;
    }

    private static bool TryValidateColorValue(string key, string value, string sourceLabel, List<string> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 && ThemePalette.IsAnsiSixteen(tokens[0]))
        {
            return true;
        }

        var suggestion = tokens.Length == 1 ? ThemePalette.SuggestColor(tokens[0]) : null;
        errors.Add(suggestion is null
            ? MessageService.GetArgsString(
                "theme-file-error-invalid-color",
                "source",
                sourceLabel,
                "key",
                key,
                "value",
                value,
                "allowed",
                string.Join(", ", ThemePalette.AnsiSixteen))
            : MessageService.GetArgsString(
                "theme-file-error-invalid-color-suggested",
                "source",
                sourceLabel,
                "key",
                key,
                "value",
                value,
                "allowed",
                string.Join(", ", ThemePalette.AnsiSixteen),
                "suggestion",
                suggestion));
        return false;
    }

    private static bool TryValidateStyleValue(string key, string value, string sourceLabel, List<string> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var colorCount = 0;
        string? badToken = null;
        var tooManyColors = false;
        foreach (var token in tokens)
        {
            if (ThemePalette.IsAnsiSixteen(token))
            {
                colorCount++;
                if (colorCount > 1)
                {
                    tooManyColors = true;
                    break;
                }

                continue;
            }

            if (!ThemePalette.IsModifier(token))
            {
                badToken = token;
                break;
            }
        }

        if (badToken is null && !tooManyColors)
        {
            return true;
        }

        var suggestion = badToken is null ? null : ThemePalette.SuggestColorOrModifier(badToken);
        errors.Add(suggestion is null
            ? MessageService.GetArgsString(
                "theme-file-error-invalid-style",
                "source",
                sourceLabel,
                "key",
                key,
                "value",
                value,
                "colors",
                string.Join(", ", ThemePalette.AnsiSixteen),
                "modifiers",
                string.Join(", ", ThemePalette.Modifiers))
            : MessageService.GetArgsString(
                "theme-file-error-invalid-style-suggested",
                "source",
                sourceLabel,
                "key",
                key,
                "value",
                value,
                "colors",
                string.Join(", ", ThemePalette.AnsiSixteen),
                "modifiers",
                string.Join(", ", ThemePalette.Modifiers),
                "suggestion",
                suggestion));
        return false;
    }

    private static string QuoteString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
