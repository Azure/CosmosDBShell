// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System.IO;

using Azure.Data.Cosmos.Shell.Core;

public class ThemeFileTests
{
    [Fact]
    public void Parse_AppliesColorsAndStylesOverDefault()
    {
        var toml = """
            name        = "custom"
            description = "Test theme"
            extends     = "default"

            [colors]
            literal       = "purple"
            error         = "red"
            bracket_cycle = ["yellow", "green"]

            [styles]
            help_header = "bold underline"
            """;

        var result = ThemeFile.Parse(toml, "memory://custom.toml", LookupBuiltIn);

        Assert.Equal("custom", result.Name);
        Assert.Equal("Test theme", result.Description);
        Assert.Equal("default", result.Extends);
        Assert.Equal("purple", result.Options.LiteralColor);
        Assert.Equal("red", result.Options.ErrorColor);
        Assert.Equal(new[] { "yellow", "green" }, result.Options.BracketCycle);
        Assert.Equal("bold underline", result.Options.HelpHeaderStyle);

        // Slots not mentioned in the file inherit from the base.
        Assert.Equal(ThemeProfiles.Default.CommandColor, result.Options.CommandColor);
        Assert.Equal(ThemeProfiles.Default.HelpNameStyle, result.Options.HelpNameStyle);
    }

    [Fact]
    public void Parse_DefaultsExtendsToDefault_WhenOmitted()
    {
        var toml = """
            name = "min"

            [colors]
            literal = "purple"
            """;

        var result = ThemeFile.Parse(toml, "memory://min.toml", LookupBuiltIn);

        Assert.Equal("default", result.Extends);
        Assert.Equal("purple", result.Options.LiteralColor);
        Assert.Equal(ThemeProfiles.Default.CommandColor, result.Options.CommandColor);
    }

    [Fact]
    public void Parse_NameDefaultsToFileNameWhenOmitted()
    {
        var toml = """
            [colors]
            error = "red"
            """;

        var result = ThemeFile.Parse(toml, "memory://my-theme.toml", LookupBuiltIn);

        Assert.Equal("my-theme", result.Name);
    }

    [Fact]
    public void Parse_RejectsUnknownColorValue()
    {
        var toml = """
            name = "bad"

            [colors]
            literal = "lightyellow3"
            """;

        var ex = Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://bad.toml", LookupBuiltIn));
        Assert.Contains("lightyellow3", ex.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownExtends()
    {
        var toml = """
            name    = "extends-missing"
            extends = "no-such-base"

            [colors]
            literal = "purple"
            """;

        var ex = Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://extends.toml", LookupBuiltIn));
        Assert.Contains("no-such-base", ex.Message);
    }

    [Fact]
    public void Parse_RejectsSelfExtends()
    {
        var toml = """
            name    = "self"
            extends = "self"

            [colors]
            literal = "purple"
            """;

        var ex = Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://self.toml", LookupBuiltIn));
        Assert.Contains("self", ex.Message);
    }

    [Fact]
    public void Parse_WarnsOnUnknownKey()
    {
        var toml = """
            name = "warn-unknown"

            [colors]
            this_is_not_a_real_slot = "red"
            """;

        var result = ThemeFile.Parse(toml, "memory://warn.toml", LookupBuiltIn);

        Assert.Single(result.Warnings);
        Assert.Contains("this_is_not_a_real_slot", result.Warnings[0]);
    }

    [Fact]
    public void Parse_RejectsEmptyBracketCycle()
    {
        var toml = """
            name = "empty-cycle"

            [colors]
            bracket_cycle = []
            """;

        Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://empty.toml", LookupBuiltIn));
    }

    [Fact]
    public void Parse_AcceptsMultiTokenStyle()
    {
        var toml = """
            name = "multi"

            [styles]
            unknown_command = "bold red"
            """;

        var result = ThemeFile.Parse(toml, "memory://multi.toml", LookupBuiltIn);

        Assert.Equal("bold red", result.Options.UnknownCommandStyle);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsModifiedSlots()
    {
        var modified = ThemeProfiles.Default with
        {
            LiteralColor = "purple",
            ContainerNameColor = "fuchsia",
            HelpHeaderStyle = "bold underline",
            BracketCycle = ["yellow", "fuchsia", "purple"],
        };

        var path = Path.Combine(Path.GetTempPath(), $"cosmos-theme-{Guid.NewGuid():N}.toml");
        try
        {
            ThemeFile.Save("round-trip", modified, ThemeProfiles.Default, path, description: "smoke", extends: "default");
            var text = File.ReadAllText(path);

            // Save should omit slots that match the baseline so the file stays minimal.
            Assert.DoesNotContain("command =", text);
            Assert.Contains("literal =", text);
            Assert.Contains("bracket_cycle =", text);
            Assert.Contains("help_header =", text);

            var result = ThemeFile.Load(path, LookupBuiltIn);
            Assert.Equal("round-trip", result.Name);
            Assert.Equal("smoke", result.Description);
            Assert.Equal("purple", result.Options.LiteralColor);
            Assert.Equal("bold underline", result.Options.HelpHeaderStyle);
            Assert.Equal(new[] { "yellow", "fuchsia", "purple" }, result.Options.BracketCycle);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ThemeOptions? LookupBuiltIn(string name)
    {
        return ThemeProfiles.All.TryGetValue(name, out var options) ? options : null;
    }
}
