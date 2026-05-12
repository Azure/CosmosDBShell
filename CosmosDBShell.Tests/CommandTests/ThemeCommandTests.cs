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
}
