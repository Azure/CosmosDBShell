// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Util;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using NSubstitute;

// Several tests below mutate process-wide environment variables (VISUAL/EDITOR).
// xUnit parallelizes across test classes, so this collection disables
// parallelization to prevent races with other tests that read those variables.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExternalEditorTestCollection
{
    public const string Name = "External editor environment tests";
}

[Collection(ExternalEditorTestCollection.Name)]
public class ExternalEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileAndThemeEditing_PauseTheExecutingShellDeadline(bool theme)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses the Windows PowerShell fake editor.");
        var directory = Directory.CreateTempSubdirectory("cosmos-editor-timeout-");
        var visual = Environment.GetEnvironmentVariable("VISUAL");
        var savedTheme = Theme.Current;
        try
        {
            var script = Path.Join(directory.FullName, "editor.ps1");
            await File.WriteAllTextAsync(script, "param($path)\nStart-Sleep -Milliseconds 1500\nexit 0\n", TestContext.Current.CancellationToken);
            var executable = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            Environment.SetEnvironmentVariable("VISUAL", $"\"{executable}\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"");
            var path = Path.Join(directory.FullName, theme ? "test.toml" : "test.csh");
            if (theme)
            {
                ThemeFile.Save("editor-timeout-test", Theme.Current, path);
            }

            using var shell = ShellInterpreter.CreateInstance();
            using var client = Substitute.For<CosmosClient>();
            client.Endpoint.Returns(new Uri("https://localhost:8081"));
            shell.State = new ConnectedState(client);
            shell.IsInputRedirected = () => false;
            shell.Options = new Program.CosmosShellOptions { Output = "json" };
            shell.LocalEmulatorInactivityTimeout = TimeSpan.FromSeconds(1);
            var command = $"{(theme ? "theme edit" : "edit")} '{path.Replace("'", "''")}'";
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var state = await shell.ExecuteCommandAsync(command, TestContext.Current.CancellationToken);

            Assert.False(state.IsError, state is ErrorCommandState error ? error.Exception.ToString() : string.Empty);
            Assert.True(watch.Elapsed > shell.LocalEmulatorInactivityTimeout);
            Assert.IsType<ConnectedState>(shell.State);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VISUAL", visual);
            Theme.Apply(savedTheme);
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Resolve_ExplicitEditor_TakesPrecedence()
    {
        var invocation = ExternalEditor.Resolve("code --wait");

        Assert.NotNull(invocation);
        Assert.Equal("code", invocation!.FileName);
        Assert.Equal("--wait", invocation.PrefixArgs);
    }

    [Fact]
    public void Resolve_QuotedExecutablePath_IsParsedAsSingleFileName()
    {
        var invocation = ExternalEditor.Resolve("\"C:\\Program Files\\App\\editor.exe\" --wait");

        Assert.NotNull(invocation);
        Assert.Equal("C:\\Program Files\\App\\editor.exe", invocation!.FileName);
        Assert.Equal("--wait", invocation.PrefixArgs);
    }

    [Fact]
    public void Resolve_FallsBackToPlatformDefault_WhenNothingConfigured()
    {
        var visual = Environment.GetEnvironmentVariable("VISUAL");
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        try
        {
            Environment.SetEnvironmentVariable("VISUAL", null);
            Environment.SetEnvironmentVariable("EDITOR", null);

            var invocation = ExternalEditor.Resolve(null);

            Assert.NotNull(invocation);
            var expected = OperatingSystem.IsWindows() ? "notepad" : "nano";
            Assert.Equal(expected, invocation!.FileName);
            Assert.Null(invocation.PrefixArgs);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VISUAL", visual);
            Environment.SetEnvironmentVariable("EDITOR", editor);
        }
    }

    [Fact]
    public void Resolve_UsesEditorEnvironmentVariable_WhenNoExplicitEditor()
    {
        var visual = Environment.GetEnvironmentVariable("VISUAL");
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        try
        {
            Environment.SetEnvironmentVariable("VISUAL", null);
            Environment.SetEnvironmentVariable("EDITOR", "vim");

            var invocation = ExternalEditor.Resolve(null);

            Assert.NotNull(invocation);
            Assert.Equal("vim", invocation!.FileName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VISUAL", visual);
            Environment.SetEnvironmentVariable("EDITOR", editor);
        }
    }

    [Fact]
    public void Resolve_WhitespaceVisual_FallsBackToEditor()
    {
        var visual = Environment.GetEnvironmentVariable("VISUAL");
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        try
        {
            // A whitespace-only VISUAL must be treated as unset so EDITOR is honored.
            Environment.SetEnvironmentVariable("VISUAL", "   ");
            Environment.SetEnvironmentVariable("EDITOR", "vim");

            var invocation = ExternalEditor.Resolve(null);

            Assert.NotNull(invocation);
            Assert.Equal("vim", invocation!.FileName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VISUAL", visual);
            Environment.SetEnvironmentVariable("EDITOR", editor);
        }
    }

    [Fact]
    public void BuildArguments_QuotesPathWithSpaces()
    {
        var invocation = ExternalEditor.EditorInvocation.Parse("code --wait");

        Assert.NotNull(invocation);
        Assert.Equal("--wait \"my script.csh\"", invocation!.BuildArguments("my script.csh"));
        Assert.Equal("--wait deploy.csh", invocation.BuildArguments("deploy.csh"));
    }

    [Fact]
    public void BuildArguments_NoPrefixArgs_ReturnsPathOnly()
    {
        var invocation = ExternalEditor.EditorInvocation.Parse("nano");

        Assert.NotNull(invocation);
        Assert.Equal("deploy.csh", invocation!.BuildArguments("deploy.csh"));
        Assert.Equal("\"a b.csh\"", invocation.BuildArguments("a b.csh"));
    }

    [Fact]
    public void DisplayName_IncludesPrefixArgs()
    {
        Assert.Equal("code --wait", ExternalEditor.EditorInvocation.Parse("code --wait")!.DisplayName);
        Assert.Equal("nano", ExternalEditor.EditorInvocation.Parse("nano")!.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ReturnsNull(string? raw)
    {
        Assert.Null(ExternalEditor.EditorInvocation.Parse(raw));
    }
}
