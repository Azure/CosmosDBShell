// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;
using Spectre.Console;

[Collection(ThemeStateTestCollection.Name)]
public class NonAnsiFallbackTests
{
    [Fact]
    public async Task RunAsync_WhenLineEditorCannotBeCreated_UsesPromptFallbackAndExitsOnEof()
    {
        var configPath = CreateConfigPath();
        var savedConsole = AnsiConsole.Console;
        var savedIn = Console.In;
        var savedOut = Console.Out;
        var savedError = Console.Error;
        using var input = new StringReader("echo hi" + Environment.NewLine);
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            var nonAnsiConsole = CreateNonAnsiConsole(output);
            AnsiConsole.Console = nonAnsiConsole;
            Console.SetIn(input);
            Console.SetOut(output);
            Console.SetError(error);

            using var shell = new ShellInterpreter(configPath)
            {
                IsInteractiveSession = static () => false,
                IsInputRedirected = static () => false,
                LineEditorTerminal = nonAnsiConsole,
            };

            await shell.RunAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Null(shell.Editor);
            Assert.False(shell.IsRunning);
            Assert.Contains("hi", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetIn(savedIn);
            Console.SetOut(savedOut);
            Console.SetError(savedError);
            AnsiConsole.Console = savedConsole;
            DeleteConfigPath(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_WhenInputIsRedirected_UsesPromptFallbackWithAnsiTerminal()
    {
        var configPath = CreateConfigPath();
        var savedConsole = AnsiConsole.Console;
        var savedIn = Console.In;
        var savedOut = Console.Out;
        using var input = new StringReader("echo hi" + Environment.NewLine);
        using var output = new StringWriter();
        try
        {
            var ansiConsole = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            });
            AnsiConsole.Console = ansiConsole;
            Console.SetIn(input);
            Console.SetOut(output);

            using var shell = new ShellInterpreter(configPath)
            {
                IsInteractiveSession = static () => false,
                IsInputRedirected = static () => true,
                LineEditorTerminal = ansiConsole,
            };

            await shell.RunAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.False(shell.IsRunning);
            Assert.Contains("hi", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetIn(savedIn);
            Console.SetOut(savedOut);
            AnsiConsole.Console = savedConsole;
            DeleteConfigPath(configPath);
        }
    }

    [Fact]
    public void Editor_WhenLineEditorCannotBeCreated_CachesFailure()
    {
        var configPath = CreateConfigPath();
        var savedError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);

            using var shell = new ShellInterpreter(configPath)
            {
                LineEditorTerminal = CreateNonAnsiConsole(output),
            };

            Assert.Null(shell.Editor);
            var errorAfterFirstAccess = error.ToString();

            Assert.Null(shell.Editor);
            Assert.Equal(errorAfterFirstAccess, error.ToString());
            Assert.Contains("ANSI", errorAfterFirstAccess, StringComparison.OrdinalIgnoreCase);
            Assert.Single(errorAfterFirstAccess.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            Console.SetError(savedError);
            DeleteConfigPath(configPath);
        }
    }

    private static IAnsiConsole CreateNonAnsiConsole(TextWriter output)
    {
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output),

            // Default enrichers force ANSI on in CI environments such as GitHub Actions.
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });
    }

    private static string CreateConfigPath()
    {
        var configPath = Path.Join(AppContext.BaseDirectory, "test-config", $"non-ansi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configPath);
        return configPath;
    }

    private static void DeleteConfigPath(string configPath)
    {
        if (Directory.Exists(configPath))
        {
            Directory.Delete(configPath, recursive: true);
        }
    }
}
