// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using CosmosShell.Tests;

[Collection(ConsoleOutputTestCollection.Name)]
public sealed class WelcomeCommandTests
{
    [Fact]
    public void WelcomeScreen_LoadsEmbeddedAnsiContent()
    {
        var expectedVersion = ShellInterpreter.GetDisplayVersion(typeof(WelcomeScreen).Assembly);

        Assert.Contains($"{MessageService.GetString("shell-welcome-preview")} {expectedVersion}", WelcomeScreen.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", WelcomeScreen.Text, StringComparison.Ordinal);
        Assert.Contains(MessageService.GetString("shell-welcome-start"), WelcomeScreen.Text, StringComparison.Ordinal);
        Assert.Contains(MessageService.GetString("shell-welcome-resources"), WelcomeScreen.Text, StringComparison.Ordinal);
        Assert.Contains("\u001b[", WelcomeScreen.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WelcomeScreen_RenderUsesLocalizedTextAndPreservesCommands()
    {
        var assembly = typeof(WelcomeScreen).Assembly;
        var resourceName = Assert.Single(assembly.GetManifestResourceNames(), name => name.EndsWith("cosmos_welcome.ans", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var keys = new HashSet<string>();

        var rendered = WelcomeScreen.Render(reader.ReadToEnd(), "1.2.3", key =>
        {
            keys.Add(key);
            return "translated:" + key;
        });

        Assert.Equal(15, keys.Count);
        Assert.DoesNotContain("{{", rendered, StringComparison.Ordinal);
        Assert.Contains("translated:shell-welcome-start", rendered, StringComparison.Ordinal);
        Assert.Contains("translated:shell-welcome-preview 1.2.3", rendered, StringComparison.Ordinal);
        Assert.Contains("connect <endpoint>", rendered, StringComparison.Ordinal);
        Assert.Contains("query \"SELECT * FROM c\"", rendered, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Azure/CosmosDBShell", rendered, StringComparison.Ordinal);
        Assert.Contains("\u001b[", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void WelcomeScreen_LongTranslationsDoNotShiftCommandsOrResourceLinks()
    {
        var assembly = typeof(WelcomeScreen).Assembly;
        var resourceName = Assert.Single(assembly.GetManifestResourceNames(), name => name.EndsWith("cosmos_welcome.ans", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var rendered = WelcomeScreen.Render(reader.ReadToEnd(), "1.2.3", key => key + new string('X', 120));
        var content = rendered[rendered.IndexOf("shell-welcome-start", StringComparison.Ordinal)..];
        Assert.DoesNotMatch(@"\x1b\[\d+C", content);
        var lines = Regex.Replace(content, @"\x1b\[[0-9;]*m", string.Empty).ReplaceLineEndings("\n").Split('\n');

        Assert.All(lines, line => Assert.True(Regex.Matches(line, "shell-welcome-").Count <= 1));
        Assert.Contains("    connect <endpoint>", lines);
        Assert.Contains("    help", lines);
        Assert.Contains("    ls  /  cd <name>  /  pwd", lines);
        Assert.Contains("    query \"SELECT * FROM c\"", lines);
        Assert.Contains("    https://github.com/Azure/CosmosDBShell", lines);
        Assert.Contains("    https://github.com/Azure/CosmosDBShell/blob/main/README.md", lines);
        Assert.Contains("    https://github.com/Azure/CosmosDBShell/tree/main/examples", lines);
    }

    [Fact]
    public void ShowWelcomeOnFirstRun_ShowsOnceAndCreatesMarker()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-welcome-{Guid.NewGuid():N}");
        var originalOut = Console.Out;
        using var output = new StringWriter();

        try
        {
            Console.SetOut(output);
            using var shell = new ShellInterpreter(configPath);
            shell.IsInteractiveSession = static () => true;

            Assert.True(shell.ShowWelcomeOnFirstRun());
            Assert.True(File.Exists(shell.WelcomeMarkerFile));
            Assert.False(shell.ShowWelcomeOnFirstRun());
            Assert.Equal(1, CountOccurrences(output.ToString(), MessageService.GetString("shell-welcome-start")));
        }
        finally
        {
            Console.SetOut(originalOut);
            TryDeleteDirectory(configPath);
        }
    }

    [Fact]
    public async Task WelcomeCommand_PrintsScreenAndReturnsSuccess()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-welcome-{Guid.NewGuid():N}");
        var originalOut = Console.Out;
        using var output = new StringWriter();

        try
        {
            Console.SetOut(output);
            using var shell = new ShellInterpreter(configPath);
            var command = new WelcomeCommand();

            var state = await command.ExecuteAsync(
                shell,
                new CommandState(),
                string.Empty,
                TestContext.Current.CancellationToken);

            Assert.False(state.IsError);
            Assert.NotNull(state.RenderUser);
            Assert.DoesNotContain(MessageService.GetString("shell-welcome-start"), output.ToString(), StringComparison.Ordinal);

            // The banner is deferred to RenderUser (invoked by PrintState based on
            // format/redirection/machine-mode), not printed unconditionally during
            // ExecuteAsync.
            state.RenderUser();
            Assert.Contains(MessageService.GetString("shell-welcome-start"), output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            TryDeleteDirectory(configPath);
        }
    }

    [Fact]
    public void PrintStartupStatus_PrintsPreviewWarningBelowVersion()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-welcome-{Guid.NewGuid():N}");
        var originalOut = Console.Out;
        using var output = new StringWriter();

        try
        {
            Console.SetOut(output);
            using var shell = new ShellInterpreter(configPath);

            shell.PrintStartupStatus();

            var status = output.ToString();
            Assert.StartsWith(
                MessageService.GetArgsString(
                    "shell-startup-status",
                    "version", ShellInterpreter.GetDisplayVersion(typeof(ShellInterpreter).Assembly),
                    "mcp_status", MessageService.GetString("shell-startup-mcp-off")),
                status,
                StringComparison.Ordinal);
            Assert.Contains(
                $"{Environment.NewLine}{MessageService.GetString("shell-startup-preview-warning")}{Environment.NewLine}",
                status,
                StringComparison.Ordinal);
            Assert.Equal(2, status.Count(character => character == '\n'));
        }
        finally
        {
            Console.SetOut(originalOut);
            TryDeleteDirectory(configPath);
        }
    }

    [Fact]
    public void ShowWelcomeOnFirstRun_SkipsWhenNonInteractive()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-welcome-{Guid.NewGuid():N}");
        var originalOut = Console.Out;
        using var output = new StringWriter();

        try
        {
            Console.SetOut(output);
            using var shell = new ShellInterpreter(configPath);
            shell.IsInteractiveSession = static () => false;

            Assert.False(shell.ShowWelcomeOnFirstRun());
            Assert.False(File.Exists(shell.WelcomeMarkerFile));
            Assert.DoesNotContain(MessageService.GetString("shell-welcome-start"), output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            TryDeleteDirectory(configPath);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static int CountOccurrences(string value, string text)
    {
        return (value.Length - value.Replace(text, string.Empty, StringComparison.Ordinal).Length) / text.Length;
    }
}
