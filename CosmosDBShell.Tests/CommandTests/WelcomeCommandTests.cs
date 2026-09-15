// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using CosmosShell.Tests;

[Collection(ConsoleOutputTestCollection.Name)]
public sealed class WelcomeCommandTests
{
    [Fact]
    public void WelcomeScreen_LoadsEmbeddedAnsiContent()
    {
        var expectedVersion = ShellInterpreter.GetDisplayVersion(typeof(WelcomeScreen).Assembly);

        Assert.Contains($"PREVIEW VERSION {expectedVersion}", WelcomeScreen.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", WelcomeScreen.Text, StringComparison.Ordinal);
        Assert.Contains("START HERE", WelcomeScreen.Text, StringComparison.Ordinal);
        Assert.Contains("RESOURCES", WelcomeScreen.Text, StringComparison.Ordinal);
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
            Assert.Equal(1, CountOccurrences(output.ToString(), "START HERE"));
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
            Assert.DoesNotContain("START HERE", output.ToString(), StringComparison.Ordinal);

            // The banner is deferred to RenderUser (invoked by PrintState based on
            // format/redirection/machine-mode), not printed unconditionally during
            // ExecuteAsync.
            state.RenderUser();
            Assert.Contains("START HERE", output.ToString(), StringComparison.Ordinal);
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
            Assert.StartsWith("Cosmos DB Shell ", status, StringComparison.Ordinal);
            Assert.Contains(" | MCP off", status, StringComparison.Ordinal);
            Assert.Contains(
                $"{Environment.NewLine}PREVIEW VERSION Commands, output, and behavior may change before general availability.{Environment.NewLine}",
                status,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Report issues", status, StringComparison.Ordinal);
            Assert.DoesNotContain("Not connected", status, StringComparison.Ordinal);
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
            Assert.DoesNotContain("START HERE", output.ToString(), StringComparison.Ordinal);
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
