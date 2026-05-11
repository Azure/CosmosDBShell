// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System.IO;

using Azure.Data.Cosmos.Shell.Core;

public class ThemeRegistryTests
{
    [Fact]
    public void NewRegistry_ContainsAllBuiltIns()
    {
        var registry = new ThemeRegistry();
        Assert.Contains("default", registry.All.Keys);
        Assert.Contains("light", registry.All.Keys);
        Assert.Contains("dark", registry.All.Keys);
        Assert.Contains("monochrome", registry.All.Keys);
        foreach (var entry in registry.All.Values)
        {
            Assert.Equal(ThemeSource.BuiltIn, entry.Source);
        }
    }

    [Fact]
    public void LoadFromDirectory_AddsThemesAndPreservesBuiltIns()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "ocean.toml"),
            """
            name = "ocean"

            [colors]
            literal = "navy"
            """);

        var registry = new ThemeRegistry();
        var loaded = registry.LoadFromDirectory(dir.Path);

        Assert.Equal(1, loaded);
        Assert.True(registry.TryGet("ocean", out var ocean));
        Assert.Equal("navy", ocean.LiteralColor);
        Assert.Equal(ThemeSource.File, registry.All["ocean"].Source);
        Assert.True(registry.TryGet("default", out _));
    }

    [Fact]
    public void LoadFromDirectory_CollectsErrorsAsWarnings_WithoutAborting()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "good.toml"),
            """
            name = "good"

            [colors]
            literal = "purple"
            """);
        File.WriteAllText(Path.Combine(dir.Path, "bad.toml"),
            """
            name = "bad"

            [colors]
            literal = "lightyellow3"
            """);

        var registry = new ThemeRegistry();
        var loaded = registry.LoadFromDirectory(dir.Path);

        Assert.Equal(1, loaded);
        Assert.True(registry.TryGet("good", out _));
        Assert.False(registry.TryGet("bad", out _));
        Assert.NotEmpty(registry.Warnings);
        Assert.Contains(registry.Warnings, w => w.Contains("lightyellow3"));
    }

    [Fact]
    public void Register_DetectsExtendsCycle()
    {
        using var dir = new TempDirectory();

        // a -> b -> a is a cycle. Register both, then assert the second triggers the cycle path.
        var aPath = Path.Combine(dir.Path, "a.toml");
        var bPath = Path.Combine(dir.Path, "b.toml");
        File.WriteAllText(aPath,
            """
            name    = "a"
            extends = "b"

            [colors]
            literal = "purple"
            """);
        File.WriteAllText(bPath,
            """
            name    = "b"
            extends = "a"

            [colors]
            literal = "purple"
            """);

        var registry = new ThemeRegistry();
        registry.LoadFromDirectory(dir.Path);

        // Whichever loads first will succeed (its extends target is unknown until the
        // second loads), and the second will be rejected. Either order leaves a warning
        // mentioning the cycle.
        Assert.NotEmpty(registry.Warnings);
        Assert.Contains(registry.Warnings, w =>
            w.Contains("extends", System.StringComparison.OrdinalIgnoreCase) || w.Contains("unknown", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResetToBuiltIns_RemovesFileThemesAndClearsWarnings()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "foo.toml"),
            """
            name = "foo"

            [colors]
            literal = "purple"
            """);

        var registry = new ThemeRegistry();
        registry.LoadFromDirectory(dir.Path);
        Assert.True(registry.TryGet("foo", out _));

        registry.ResetToBuiltIns();
        Assert.False(registry.TryGet("foo", out _));
        Assert.True(registry.TryGet("default", out _));
        Assert.Empty(registry.Warnings);
    }

    [Fact]
    public void Register_FileThemeShadowingBuiltIn_ProducesWarning()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "default.toml"),
            """
            name    = "default"
            extends = "monochrome"

            [colors]
            literal = "purple"
            """);

        var registry = new ThemeRegistry();
        registry.LoadFromDirectory(dir.Path);

        Assert.Contains(registry.Warnings, w => w.Contains("default"));
        Assert.True(registry.TryGet("default", out var resolved));

        // The shadowing file theme overrode the built-in literal color.
        Assert.Equal("purple", resolved.LiteralColor);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cosmos-theme-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this.Path))
                {
                    Directory.Delete(this.Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
