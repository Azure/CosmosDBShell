// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.IO;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class ThemeCommandTests
{
    [Fact]
    public async Task Validate_ParsesThemeWithoutRegisteringOrApplying()
    {
        var name = $"validate-{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), name + ".toml");
        var saved = Theme.Current;
        try
        {
            File.WriteAllText(path,
                $$"""
                name = "{{name}}"

                [colors]
                literal = "purple"

                [styles]
                unknown_command = "bold red"
                """);

            var command = new ThemeCommand
            {
                Action = "validate",
                Name = path,
            };

            var state = await command.ExecuteAsync(ShellInterpreter.Instance, new CommandState(), "", CancellationToken.None);
            var json = Assert.IsType<ShellJson>(state.Result);

            Assert.True(state.IsPrinted);
            Assert.True(json.Value.GetProperty("valid").GetBoolean());
            Assert.Equal(name, json.Value.GetProperty("name").GetString());
            Assert.False(ThemeProfiles.TryGet(name, out _));
            Assert.Same(saved, Theme.Current);
        }
        finally
        {
            Theme.Apply(saved);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Validate_ScansDirectoryAndReportsCounts()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "ok.toml"),
            """
            name = "ok"

            [colors]
            literal = "purple"
            """);
        File.WriteAllText(Path.Combine(dir.Path, "broken.toml"),
            """
            name = "broken"

            [colors]
            literal = "lightyellow3"
            """);

        var command = new ThemeCommand
        {
            Action = "validate",
            Name = dir.Path,
        };

        var state = await command.ExecuteAsync(ShellInterpreter.Instance, new CommandState(), "", CancellationToken.None);
        var error = Assert.IsType<ErrorCommandState>(state);

        Assert.Contains("1 of 2", error.Exception.Message);
    }

    [Fact]
    public async Task Validate_StrictTreatsWarningsAsErrors()
    {
        var name = $"strict-{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), name + ".toml");
        try
        {
            File.WriteAllText(path,
                $$"""
                name = "{{name}}"

                [colors]
                this_is_not_a_real_slot = "red"
                """);

            var command = new ThemeCommand
            {
                Action = "validate",
                Name = path,
                Strict = true,
            };

            var state = await command.ExecuteAsync(ShellInterpreter.Instance, new CommandState(), "", CancellationToken.None);
            var error = Assert.IsType<ErrorCommandState>(state);
            Assert.Contains("strict mode", error.Exception.Message);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cosmos-theme-validate-{Guid.NewGuid():N}");
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
