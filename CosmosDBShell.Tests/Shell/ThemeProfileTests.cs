// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System.Collections.Generic;
using System.Linq;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Verifies the built-in <see cref="ThemeProfiles"/> match their contracts and
/// guards against accidental regressions to non-themable color names.
/// </summary>
public class ThemeProfileTests
{
    /// <summary>
    /// Spectre's standard ANSI 16 color names. Anything outside this set (plus the
    /// empty string and the dim/bold/underline modifiers) bypasses terminal theming
    /// and breaks the appearance contract for at least one terminal background.
    /// </summary>
    private static readonly HashSet<string> Ansi16 = new(StringComparer.OrdinalIgnoreCase)
    {
        "black", "maroon", "green", "olive", "navy", "purple", "teal", "silver",
        "grey", "red", "lime", "yellow", "blue", "fuchsia", "aqua", "white",
    };

    private static readonly HashSet<string> AllowedModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "bold", "dim", "italic", "underline", "strikethrough", "invert", "conceal",
        "slowblink", "rapidblink",
    };

    [Theory]
    [InlineData("default")]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("monochrome")]
    public void TryGet_KnownProfile_Resolves(string name)
    {
        Assert.True(ThemeProfiles.TryGet(name, out var profile));
        Assert.NotNull(profile);
    }

    [Fact]
    public void TryGet_Unknown_FallsBackToDefault()
    {
        Assert.False(ThemeProfiles.TryGet("not-a-theme", out var profile));
        Assert.Same(ThemeProfiles.Default, profile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGet_BlankName_FallsBackToDefault(string? name)
    {
        Assert.False(ThemeProfiles.TryGet(name, out var profile));
        Assert.Same(ThemeProfiles.Default, profile);
    }

    [Fact]
    public void Apply_ChangesActiveTheme_AndRoundTrips()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Apply(ThemeProfiles.Light);
            Assert.Same(ThemeProfiles.Light, Theme.Current);

            Theme.Apply(ThemeProfiles.Default);
            Assert.Same(ThemeProfiles.Default, Theme.Current);
        }
        finally
        {
            Theme.Apply(saved);
        }
    }

    [Fact]
    public void Apply_AffectsHelperOutput()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Apply(ThemeProfiles.Default);
            var defaultMarkup = Theme.FormatCommand("connect");
            Assert.Contains("[" + ThemeProfiles.Default.CommandColor + "]", defaultMarkup);

            Theme.Apply(ThemeProfiles.Monochrome);
            var monochromeMarkup = Theme.FormatCommand("connect");

            // Monochrome empties the command color, so Format helpers must emit the
            // bare escaped text with no markup tag at all.
            Assert.Equal("connect", monochromeMarkup);
        }
        finally
        {
            Theme.Apply(saved);
        }
    }

    [Fact]
    public void Monochrome_EmitsNoForegroundColor()
    {
        var profile = ThemeProfiles.Monochrome;
        Assert.Empty(profile.CommandColor);
        Assert.Empty(profile.ConnectedPromptColor);
        Assert.Empty(profile.LiteralColor);
        Assert.Empty(profile.ErrorColor);
        Assert.Equal("bold", profile.HelpHeaderStyle);
        Assert.Equal("bold", profile.UnknownCommandStyle);
    }

    /// <summary>
    /// Lint guard: every color slot in every built-in profile must be either empty
    /// (= terminal default) or a Spectre name in the ANSI 16 set, optionally combined
    /// with allowed modifiers. Catches accidental reintroduction of fixed-256 names
    /// like <c>cyan</c>, <c>magenta</c>, <c>violet</c>, <c>plum3</c>, etc.
    /// </summary>
    [Theory]
    [InlineData("default")]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("monochrome")]
    public void Profiles_UseOnlyAnsi16ColorsAndAllowedModifiers(string name)
    {
        Assert.True(ThemeProfiles.TryGet(name, out var profile));

        var slots = new (string Slot, string Value)[]
        {
            (nameof(profile.CommandColor), profile.CommandColor),
            (nameof(profile.UnknownCommandStyle), profile.UnknownCommandStyle),
            (nameof(profile.ArgumentNameColor), profile.ArgumentNameColor),
            (nameof(profile.ConnectedPromptColor), profile.ConnectedPromptColor),
            (nameof(profile.DatabaseNameColor), profile.DatabaseNameColor),
            (nameof(profile.ContainerNameColor), profile.ContainerNameColor),
            (nameof(profile.RedirectionColor), profile.RedirectionColor),
            (nameof(profile.JsonPropertyColor), profile.JsonPropertyColor),
            (nameof(profile.JsonPunctuationColor), profile.JsonPunctuationColor),
            (nameof(profile.LiteralColor), profile.LiteralColor),
            (nameof(profile.KeywordColor), profile.KeywordColor),
            (nameof(profile.ErrorColor), profile.ErrorColor),
            (nameof(profile.OperatorColor), profile.OperatorColor),
            (nameof(profile.TableValueColor), profile.TableValueColor),
            (nameof(profile.WarningColor), profile.WarningColor),
            (nameof(profile.DirectoryColor), profile.DirectoryColor),
            (nameof(profile.MutedColor), profile.MutedColor),
            (nameof(profile.HelpAccentColor), profile.HelpAccentColor),
            (nameof(profile.HelpPlaceholderColor), profile.HelpPlaceholderColor),
            (nameof(profile.HelpVariableColor), profile.HelpVariableColor),
            (nameof(profile.HelpHeaderStyle), profile.HelpHeaderStyle),
            (nameof(profile.HelpNameStyle), profile.HelpNameStyle),
        };

        foreach (var (slot, value) in slots)
        {
            AssertStyle(slot, value, name);
        }

        foreach (var color in profile.BracketCycle)
        {
            AssertStyle(nameof(profile.BracketCycle), color, name);
        }

        Assert.NotEmpty(profile.BracketCycle);
    }

    [Fact]
    public void ThemeLocalizationKeys_AreDefined()
    {
        // Guards against typos in the keys consumed by ThemeCommand and Program.ApplyTheme.
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("help-Theme")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("warning-unknown-theme")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-theme-description")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-theme-active")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-theme-applied")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-theme-sample-heading")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-theme-unknown")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-theme-unknown-action")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-theme-use-missing-name")));
    }

    /// <summary>
    /// Regression: every built-in profile must produce well-balanced Spectre markup
    /// for every <c>Theme.Format*</c> helper. Caught the original monochrome bug
    /// where an "open tag only" accessor combined with a hardcoded <c>[/]</c> emitted
    /// a stray closing tag that crashed Spectre's markup parser.
    /// </summary>
    [Theory]
    [InlineData("default")]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("monochrome")]
    public void Format_ProducesParseableMarkup_ForEveryProfile(string name)
    {
        Assert.True(ThemeProfiles.TryGet(name, out var profile));

        var saved = Theme.Current;
        try
        {
            Theme.Apply(profile);

            var samples = new (string Role, string Markup)[]
            {
                ("FormatCommand", Theme.FormatCommand("connect")),
                ("FormatUnknownCommand", Theme.FormatUnknownCommand("nope")),
                ("FormatScriptPath", Theme.FormatScriptPath("seed.csh")),
                ("FormatArgumentName", Theme.FormatArgumentName("--max")),
                ("ConnectedStatePromt", Theme.ConnectedStatePromt("CS >")),
                ("DatabaseNamePromt", Theme.DatabaseNamePromt("MyDb")),
                ("ContainerNamePromt", Theme.ContainerNamePromt("MyContainer")),
                ("FormatRedirection", Theme.FormatRedirection(">>")),
                ("FormatRedirectionDestination", Theme.FormatRedirectionDestination("out.json")),
                ("FormatJsonProperty", Theme.FormatJsonProperty("\"id\"")),
                ("FormatJsonBracket", Theme.FormatJsonBracket(":")),
                ("FormatJsonString", Theme.FormatJsonString("\"hello\"")),
                ("FormatJsonNumber", Theme.FormatJsonNumber("42")),
                ("FormatJsonBoolean", Theme.FormatJsonBoolean("true")),
                ("FormatJsonNull", Theme.FormatJsonNull("null")),
                ("FormatStringLiteral", Theme.FormatStringLiteral("\"hello\"")),
                ("FormatNumberLiteral", Theme.FormatNumberLiteral("42")),
                ("FormatBooleanLiteral", Theme.FormatBooleanLiteral("true")),
                ("FormatKeyword", Theme.FormatKeyword("if")),
                ("FormatError", Theme.FormatError("oops")),
                ("FormatOperator", Theme.FormatOperator("+")),
                ("FormatTableValue", Theme.FormatTableValue("West US")),
                ("FormatTableValueRaw", Theme.FormatTableValueRaw("123")),
                ("FormatWarning", Theme.FormatWarning("careful")),
                ("FormatDirectory", Theme.FormatDirectory("docs")),
                ("FormatMuted", Theme.FormatMuted("2026-05-11")),
                ("FormatHelpHeader", Theme.FormatHelpHeader("Connection")),
                ("FormatHelpName", Theme.FormatHelpName("--theme")),
                ("FormatHelpDescription", Theme.FormatHelpDescription("Switches the active color theme.")),
                ("FormatBracket(0)", Theme.FormatBracket("{", 0)),
                ("FormatBracket(1)", Theme.FormatBracket("[", 1)),
                ("FormatBracket(2)", Theme.FormatBracket("(", 2)),
            };

            foreach (var (role, markup) in samples)
            {
                try
                {
                    _ = new Spectre.Console.Markup(markup);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Profile '{name}' produced unparseable markup for {role}: '{markup}'. {ex.Message}");
                }
            }
        }
        finally
        {
            Theme.Apply(saved);
        }
    }

    private static void AssertStyle(string slot, string value, string profile)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(tokens);

        foreach (var token in tokens)
        {
            var ok = Ansi16.Contains(token) || AllowedModifiers.Contains(token);
            Assert.True(
                ok,
                $"Profile '{profile}' slot '{slot}' uses non-ANSI-16 token '{token}' (full value: '{value}'). " +
                "Only ANSI 16 color names and Spectre style modifiers are allowed in built-in profiles.");
        }
    }
}
