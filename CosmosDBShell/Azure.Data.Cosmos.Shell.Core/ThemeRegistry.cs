// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System.Collections.Generic;
using System.IO;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Holds the merged set of built-in (<see cref="ThemeProfiles"/>) and file-loaded
/// theme profiles. Built-ins are always present; user files in the configured
/// directory are scanned at startup and via <see cref="LoadFromDirectory"/>.
/// File-defined themes may shadow built-ins by name (a warning is recorded).
/// </summary>
internal sealed class ThemeRegistry
{
    private readonly Dictionary<string, ThemeRegistration> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> warnings = new();

    public ThemeRegistry()
    {
        foreach (var (name, options) in ThemeProfiles.All)
        {
            this.entries[name] = new ThemeRegistration(
                name,
                options,
                Source: ThemeSource.BuiltIn,
                Path: null,
                Description: null);
        }
    }

    /// <summary>
    /// Singleton accessor used by <see cref="ThemeProfiles.TryGet"/> so the legacy
    /// API surface keeps working after the registry is introduced.
    /// </summary>
    public static ThemeRegistry Instance { get; } = new();

    /// <summary>All registered themes (built-ins followed by file themes), keyed by name.</summary>
    public IReadOnlyDictionary<string, ThemeRegistration> All => this.entries;

    /// <summary>Diagnostic warnings emitted by the most recent load operation.</summary>
    public IReadOnlyList<string> Warnings => this.warnings;

    /// <summary>
    /// Attempts to resolve a theme by name. Returns <c>true</c> on success; on unknown
    /// name returns <c>false</c> and yields the default profile via <paramref name="profile"/>.
    /// </summary>
    public bool TryGet(string? name, out ThemeOptions profile)
    {
        if (!string.IsNullOrWhiteSpace(name) && this.entries.TryGetValue(name, out var entry))
        {
            profile = entry.Options;
            return true;
        }

        profile = ThemeProfiles.Default;
        return false;
    }

    /// <summary>
    /// Registers a single file-loaded theme. Throws <see cref="ThemeLoadException"/>
    /// on cycles or unresolvable <c>extends</c> chains.
    /// </summary>
    public void Register(ThemeFileResult result)
    {
        if (this.entries.TryGetValue(result.Name, out var existing) && existing.Source == ThemeSource.BuiltIn)
        {
            this.warnings.Add(MessageService.GetString(
                "theme-registry-warning-shadow-builtin",
                MessageService.Args("name", result.Name, "source", result.Source)));
        }

        // Validate that a multi-level extends chain terminates at a known base.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { result.Name };
        string cursor = result.Extends;
        for (var hops = 0; hops < ThemeFile.MaxResolveDepth; hops++)
        {
            if (!visited.Add(cursor))
            {
                throw new ThemeLoadException(MessageService.GetString(
                    "theme-file-error-extends-cycle",
                    MessageService.Args("source", result.Source, "name", result.Name)));
            }

            if (!this.entries.TryGetValue(cursor, out var parent))
            {
                throw new ThemeLoadException(MessageService.GetArgsString(
                    "theme-file-error-extends-unknown",
                    "source",
                    result.Source,
                    "name",
                    cursor));
            }

            if (parent.Source == ThemeSource.BuiltIn || parent.Extends == null)
            {
                this.entries[result.Name] = new ThemeRegistration(
                    result.Name,
                    result.Options,
                    Source: ThemeSource.File,
                    Path: result.Source,
                    Description: result.Description)
                {
                    Extends = result.Extends,
                };

                foreach (var warning in result.Warnings)
                {
                    this.warnings.Add(warning);
                }

                return;
            }

            cursor = parent.Extends!;
        }

        throw new ThemeLoadException(MessageService.GetString(
            "theme-file-error-extends-too-deep",
            MessageService.Args("source", result.Source, "name", result.Name)));
    }

    /// <summary>
    /// Removes every file-loaded theme and clears recorded warnings, leaving the
    /// built-ins intact. Used by <see cref="LoadFromDirectory"/> to give callers
    /// a clean reload semantics.
    /// </summary>
    public void ResetToBuiltIns()
    {
        this.warnings.Clear();
        var fileNames = this.entries
            .Where(kv => kv.Value.Source == ThemeSource.File)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var name in fileNames)
        {
            this.entries.Remove(name);
        }
    }

    /// <summary>
    /// Scans <paramref name="directory"/> for <c>*.toml</c> files and registers each
    /// successfully-loaded theme. Failures (parse errors, invalid colors, missing
    /// extends) are collected as warnings and do not abort the scan.
    /// </summary>
    public int LoadFromDirectory(string directory)
    {
        var loaded = 0;
        foreach (var path in ThemeFile.EnumerateThemeFiles(directory))
        {
            try
            {
                var result = ThemeFile.Load(path, name => this.TryLookupOptions(name));
                this.Register(result);
                loaded++;
            }
            catch (ThemeLoadException ex)
            {
                this.warnings.Add(ex.Message);
            }
            catch (Exception ex)
            {
                this.warnings.Add(MessageService.GetArgsString(
                    "theme-registry-warning-load-failed",
                    "path",
                    path,
                    "message",
                    ex.Message));
            }
        }

        return loaded;
    }

    /// <summary>
    /// Loads (or replaces) a single theme file ad-hoc. Used by <c>theme load</c>.
    /// </summary>
    public ThemeFileResult LoadFile(string path)
    {
        var result = ThemeFile.Load(path, name => this.TryLookupOptions(name));
        this.Register(result);
        return result;
    }

    /// <summary>
    /// Validates a single theme file without registering it or changing the active theme.
    /// </summary>
    public ThemeFileResult ValidateFile(string path)
    {
        return ThemeFile.Load(path, name => this.TryLookupOptions(name));
    }

    private ThemeOptions? TryLookupOptions(string name)
    {
        return this.entries.TryGetValue(name, out var registration) ? registration.Options : null;
    }
}
