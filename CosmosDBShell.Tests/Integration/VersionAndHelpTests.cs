// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

public class VersionAndHelpTests : IntegrationTestBase
{
    [Fact]
    public async Task Version_ReturnsStructuredJson()
    {
        var state = await RunScriptAsync("version");

        Assert.False(state.IsError);

        var json = GetJson(state);
        Assert.True(json.TryGetProperty("version", out var version));
        Assert.False(string.IsNullOrWhiteSpace(version.GetString()));
        Assert.True(json.TryGetProperty("mcpEnabled", out var mcpEnabled));
        Assert.False(mcpEnabled.GetBoolean());
        Assert.Equal("off", json.GetProperty("mcpStatus").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("mcpPort").ValueKind);
    }

    [Fact]
    public async Task Help_NoArgs_ReturnsCommandAndStatementMetadata()
    {
        var state = await RunScriptAsync("help");

        Assert.False(state.IsError);

        var json = GetJson(state);
        Assert.Contains("available", json.GetProperty("help").GetString(), StringComparison.OrdinalIgnoreCase);

        var commands = json.GetProperty("commands");
        Assert.True(commands.GetArrayLength() > 0);
        Assert.Contains(commands.EnumerateArray(), command => command.GetProperty("command").GetString() == "help");
        Assert.Contains(commands.EnumerateArray(), command => command.GetProperty("command").GetString() == "version");

        var statements = json.GetProperty("statements");
        Assert.True(statements.GetArrayLength() > 0);

        Assert.False(json.TryGetProperty("keyboardShortcuts", out _));
        var topics = json.GetProperty("topics");
        Assert.Contains(topics.EnumerateArray(), topic => topic.GetProperty("topic").GetString() == "shortcuts");
    }

    [Fact]
    public async Task Help_SpecificCommand_ReturnsDetailedCommandMetadata()
    {
        var state = await RunScriptAsync("help echo");

        Assert.False(state.IsError);

        var json = GetJson(state);
        Assert.Equal("echo", json.GetProperty("command").GetString());
        Assert.Contains("message", json.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);

        var parameters = json.GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "messages");
    }

    [Theory]
    [InlineData("help shortcuts")]
    [InlineData("help --plain shortcuts")]
    public async Task Help_ShortcutsTopic_ReturnsKeyboardShortcutMetadata(string script)
    {
        var state = await RunScriptAsync(script);

        Assert.False(state.IsError);
        var json = GetJson(state);
        Assert.Equal("shortcuts", json.GetProperty("topic").GetString());
        var shortcuts = json.GetProperty("keyboardShortcuts");
        var expectedShortcuts = new[]
        {
            "Up / Down", "Ctrl+P / Ctrl+N", "Ctrl+R", "Ctrl+S",
            "Ctrl+B / Ctrl+F", "Ctrl+A", "Ctrl+E",
            "Tab / Ctrl+Tab", "Ctrl+U", "Ctrl+K", "Ctrl+W", "Esc",
            "Ctrl+L", "Ctrl+C", "Ctrl+D",
        };
        Assert.Equal(expectedShortcuts, shortcuts.EnumerateArray().Select(shortcut => shortcut.GetProperty("shortcut").GetString()));
        Assert.All(shortcuts.EnumerateArray(), shortcut =>
        {
            Assert.False(string.IsNullOrWhiteSpace(shortcut.GetProperty("group").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(shortcut.GetProperty("description").GetString()));
        });
        Assert.Contains("Ctrl+G", json.GetProperty("historySearch").GetString(), StringComparison.Ordinal);
        Assert.Contains(shortcuts.EnumerateArray(), shortcut =>
            shortcut.GetProperty("shortcut").GetString() == "Ctrl+R" &&
            shortcut.GetProperty("description").GetString()!.Contains("older matches", StringComparison.Ordinal));
        Assert.Contains(shortcuts.EnumerateArray(), shortcut =>
            shortcut.GetProperty("shortcut").GetString() == "Ctrl+S" &&
            shortcut.GetProperty("description").GetString()!.Contains("newer matches", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Help_UnknownCommand_ReturnsError()
    {
        var state = await RunScriptAsync("help nonexistent");

        Assert.True(state.IsError);
        Assert.Contains("nonexistent", GetErrorMessage(state), StringComparison.OrdinalIgnoreCase);
    }
}
