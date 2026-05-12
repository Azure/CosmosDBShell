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
    public void Parse_RejectsModifierInColorSlot()
    {
        var toml = """
            name = "bad-color"

            [colors]
            literal = "bold purple"
            """;

        var ex = Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://bad-color.toml", LookupBuiltIn));
        Assert.Contains("literal", ex.Message);
        Assert.Contains("bold purple", ex.Message);
    }

    [Fact]
    public void Parse_RejectsMultipleColorsInStyleSlot()
    {
        var toml = """
            name = "bad-style"

            [styles]
            unknown_command = "bold red yellow"
            """;

        var ex = Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://bad-style.toml", LookupBuiltIn));
        Assert.Contains("unknown_command", ex.Message);
        Assert.Contains("bold red yellow", ex.Message);
    }

    [Fact]
    public void Parse_AggregatesMultipleInvalidValues()
    {
        var toml = """
            name = "many-bad"

            [colors]
            literal = "lightyellow3"
            error   = "magneta"

            [styles]
            unknown_command = "bld"
            """;

        var ex = Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://many.toml", LookupBuiltIn));
        Assert.Contains("literal", ex.Message);
        Assert.Contains("error", ex.Message);
        Assert.Contains("unknown_command", ex.Message);
    }

    [Fact]
    public void Parse_SuggestsClosestColor()
    {
        var toml = """
            name = "typo"

            [colors]
            literal = "purpel"
            """;

        var ex = Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://typo.toml", LookupBuiltIn));
        Assert.Contains("Did you mean 'purple'", ex.Message);
    }

    [Fact]
    public void Parse_SuggestsClosestStyleToken()
    {
        var toml = """
            name = "typo-style"

            [styles]
            unknown_command = "bld red"
            """;

        var ex = Assert.Throws<ThemeLoadException>(() => ThemeFile.Parse(toml, "memory://typo-style.toml", LookupBuiltIn));
        Assert.Contains("Did you mean 'bold'", ex.Message);
    }

    [Fact]
    public void Parse_WarnsOnSingleEntryBracketCycle()
    {
        var toml = """
            name = "single-cycle"

            [colors]
            bracket_cycle = ["yellow"]
            """;

        var result = ThemeFile.Parse(toml, "memory://single-cycle.toml", LookupBuiltIn);

        Assert.Contains(result.Warnings, w => w.Contains("only one bracket_cycle color"));
        Assert.Equal(new[] { "yellow" }, result.Options.BracketCycle);
    }

    [Fact]
    public void Parse_WarnsOnDuplicateBracketCycle()
    {
        var toml = """
            name = "dup-cycle"

            [colors]
            bracket_cycle = ["yellow", "yellow", "aqua"]
            """;

        var result = ThemeFile.Parse(toml, "memory://dup-cycle.toml", LookupBuiltIn);

        Assert.Contains(result.Warnings, w => w.Contains("duplicate bracket_cycle"));
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
            ThemeFile.Save("round-trip", modified, path, description: "smoke");
            var text = File.ReadAllText(path);

            // Save now writes the full theme self-contained, so every slot appears
            // and no 'extends' line is emitted. The file does not depend on any
            // other profile being present at load time.
            Assert.Contains("command = ", text);
            Assert.Contains("literal = ", text);
            Assert.Contains("bracket_cycle = ", text);
            Assert.Contains("help_header = ", text);
            Assert.DoesNotContain("extends ", text);

            var result = ThemeFile.Load(path, LookupBuiltIn);
            Assert.Equal("round-trip", result.Name);
            Assert.Equal("smoke", result.Description);
            Assert.Equal("purple", result.Options.LiteralColor);
            Assert.Equal("fuchsia", result.Options.ContainerNameColor);
            Assert.Equal("bold underline", result.Options.HelpHeaderStyle);
            Assert.Equal(new[] { "yellow", "fuchsia", "purple" }, result.Options.BracketCycle);

            // Slots that match the default still round-trip cleanly because they're
            // written explicitly.
            Assert.Equal(ThemeProfiles.Default.CommandColor, result.Options.CommandColor);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Save_WithoutExtends_ProducesSelfContainedFile()
    {
        // A saved file should load even when no base profiles are available
        // (lookup returns null for everything). The current behavior is "extends
        // defaults to 'default'", so a custom lookup that knows nothing must still
        // be able to load the file's own values for every populated slot.
        var path = Path.Combine(Path.GetTempPath(), $"cosmos-theme-{Guid.NewGuid():N}.toml");
        try
        {
            ThemeFile.Save("standalone", ThemeProfiles.Light, path);

            // Use a baseline-only-empty lookup that maps "default" to an empty
            // ThemeOptions record. Nothing in the saved file relies on default's
            // contents because every slot is written explicitly.
            var emptyDefault = new ThemeOptions
            {
                CommandColor = string.Empty,
                ConnectedPromptColor = string.Empty,
                DatabaseNameColor = string.Empty,
                ContainerNameColor = string.Empty,
                LiteralColor = string.Empty,
                BracketCycle = new[] { string.Empty },
            };
            var result = ThemeFile.Load(path, name => name == "default" ? emptyDefault : null);

            Assert.Equal(ThemeProfiles.Light.LiteralColor, result.Options.LiteralColor);
            Assert.Equal(ThemeProfiles.Light.BracketCycle, result.Options.BracketCycle);
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
