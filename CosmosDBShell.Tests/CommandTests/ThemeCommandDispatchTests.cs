// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.IO;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Offline tests for <see cref="ThemeCommand"/>'s action dispatch. These cover the
/// read-only actions (current, list, show), the in-memory theme switching actions
/// (use/set), and the file-backed load/save/validate/reload branches, all without
/// launching an external editor or file browser. Tests that mutate the global
/// <see cref="Theme.Current"/> save and restore it, and use unique file names so
/// they do not collide with the real user themes directory.
/// </summary>
[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class ThemeCommandDispatchTests
{
    private const string ValidToml =
        """
        name = "placeholder"

        [colors]
        literal = "purple"
        """;

    [Fact]
    public async Task NoAction_DefaultsToCurrent()
    {
        var state = await RunAsync(new ThemeCommand());

        Assert.True(state.IsPrinted);
        var json = Assert.IsType<ShellJson>(state.Result);
        Assert.True(json.Value.TryGetProperty("active", out _));
    }

    [Fact]
    public async Task Current_ReturnsActiveThemeName()
    {
        var state = await RunAsync(new ThemeCommand { Action = "current" });

        var json = Assert.IsType<ShellJson>(state.Result);
        Assert.False(string.IsNullOrEmpty(json.Value.GetProperty("active").GetString()));
    }

    [Fact]
    public async Task List_IncludesBuiltInThemes()
    {
        var state = await RunAsync(new ThemeCommand { Action = "list" });

        var json = Assert.IsType<ShellJson>(state.Result);
        var names = json.Value.GetProperty("themes").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("default", names);
        Assert.Contains("light", names);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("light")]
    [InlineData("monochrome")]
    public async Task Show_KnownTheme_PreviewsWithoutChangingActive(string name)
    {
        var saved = Theme.Current;
        try
        {
            var state = await RunAsync(new ThemeCommand { Action = "show", Name = name });

            var json = Assert.IsType<ShellJson>(state.Result);
            Assert.Equal(name, json.Value.GetProperty("previewed").GetString());
            Assert.Same(saved, Theme.Current);
        }
        finally
        {
            Theme.Apply(saved);
        }
    }

    [Fact]
    public async Task Show_UnknownTheme_ReturnsError()
    {
        var state = await RunAsync(new ThemeCommand { Action = "show", Name = "does-not-exist-xyz" });

        Assert.IsType<ErrorCommandState>(state);
    }

    [Fact]
    public async Task Use_KnownTheme_AppliesIt()
    {
        var saved = Theme.Current;
        try
        {
            var state = await RunAsync(new ThemeCommand { Action = "use", Name = "light" });

            var json = Assert.IsType<ShellJson>(state.Result);
            Assert.Equal("light", json.Value.GetProperty("applied").GetString());
        }
        finally
        {
            Theme.Apply(saved);
        }
    }

    [Fact]
    public async Task Set_AliasOfUse_AppliesTheme()
    {
        var saved = Theme.Current;
        try
        {
            var state = await RunAsync(new ThemeCommand { Action = "set", Name = "dark" });

            var json = Assert.IsType<ShellJson>(state.Result);
            Assert.Equal("dark", json.Value.GetProperty("applied").GetString());
        }
        finally
        {
            Theme.Apply(saved);
        }
    }

    [Fact]
    public async Task Use_MissingName_ReturnsError()
    {
        var state = await RunAsync(new ThemeCommand { Action = "use" });

        Assert.IsType<ErrorCommandState>(state);
    }

    [Fact]
    public async Task Use_UnknownName_ReturnsError()
    {
        var state = await RunAsync(new ThemeCommand { Action = "use", Name = "nope-xyz" });

        Assert.IsType<ErrorCommandState>(state);
    }

    [Fact]
    public async Task Load_MissingPath_ReturnsError()
    {
        var state = await RunAsync(new ThemeCommand { Action = "load" });

        Assert.IsType<ErrorCommandState>(state);
    }

    [Fact]
    public async Task Load_ValidFile_LoadsAndApplies()
    {
        var name = $"load-{Guid.NewGuid():N}";
        var path = WriteTempToml(name, ValidToml);
        var saved = Theme.Current;
        try
        {
            var state = await RunAsync(new ThemeCommand { Action = "load", Name = path });

            var json = Assert.IsType<ShellJson>(state.Result);
            Assert.Equal("placeholder", json.Value.GetProperty("loaded").GetString());
        }
        finally
        {
            Theme.Apply(saved);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_NonExistentFile_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.toml");

        var state = await RunAsync(new ThemeCommand { Action = "load", Name = path });

        Assert.IsType<ErrorCommandState>(state);
    }

    [Fact]
    public async Task Load_InvalidToml_ReturnsError()
    {
        var path = WriteTempToml($"bad-{Guid.NewGuid():N}", "this is = = not valid toml [[[");
        try
        {
            var state = await RunAsync(new ThemeCommand { Action = "load", Name = path });

            Assert.IsType<ErrorCommandState>(state);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Save_MissingName_ReturnsError()
    {
        var state = await RunAsync(new ThemeCommand { Action = "save" });

        Assert.IsType<ErrorCommandState>(state);
    }

    [Fact]
    public async Task Save_InvalidName_ReturnsError()
    {
        var state = await RunAsync(new ThemeCommand { Action = "save", Name = "../escape" });

        Assert.IsType<ErrorCommandState>(state);
    }

    [Fact]
    public async Task Save_ToExplicitPath_WritesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"save-{Guid.NewGuid():N}.toml");
        try
        {
            var state = await RunAsync(new ThemeCommand { Action = "save", Name = "exported", Path = path });

            var json = Assert.IsType<ShellJson>(state.Result);
            Assert.Equal("exported", json.Value.GetProperty("saved").GetString());
            Assert.True(File.Exists(path));
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
    public async Task Save_ExistingFileWithoutForce_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"save-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, "name = \"existing\"");
        try
        {
            var state = await RunAsync(new ThemeCommand { Action = "save", Name = "exported", Path = path });

            Assert.IsType<ErrorCommandState>(state);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Save_ExistingFileWithForce_Overwrites()
    {
        var path = Path.Combine(Path.GetTempPath(), $"save-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, "name = \"existing\"");
        try
        {
            var state = await RunAsync(new ThemeCommand { Action = "save", Name = "exported", Path = path, Force = true });

            var json = Assert.IsType<ShellJson>(state.Result);
            Assert.Equal("exported", json.Value.GetProperty("saved").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Reload_RescansUserThemesDirectory()
    {
        var state = await RunAsync(new ThemeCommand { Action = "reload" });

        var json = Assert.IsType<ShellJson>(state.Result);
        Assert.True(json.Value.TryGetProperty("reloaded", out _));
    }

    [Fact]
    public async Task UnknownAction_ReturnsError()
    {
        var state = await RunAsync(new ThemeCommand { Action = "frobnicate" });

        Assert.IsType<ErrorCommandState>(state);
    }

    private static Task<CommandState> RunAsync(ThemeCommand command) =>
        command.ExecuteAsync(ShellInterpreter.Instance, new CommandState(), string.Empty, CancellationToken.None);

    private static string WriteTempToml(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), name + ".toml");
        File.WriteAllText(path, content);
        return path;
    }
}
