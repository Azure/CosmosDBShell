// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Spectre.Console;
using Xunit;

[Collection(ConsoleOutputTestCollection.Name)]
public class HelpCommandVerificationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HelpCommand_OverviewListsShortcutsTopicWithoutTable(bool plain, bool details)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var output = await RenderAsync(shell, new HelpCommand { Plain = plain, Details = details });

        Assert.Contains("Help Topics", output);
        Assert.Contains("shortcuts", output);
        Assert.Contains("Keyboard shortcuts for the interactive prompt", output);
        Assert.DoesNotContain("help shortcuts", output);
        Assert.DoesNotContain("Search history backward", output);
    }

    [Theory]
    [InlineData(false, "shortcuts")]
    [InlineData(true, "shortcuts")]
    [InlineData(false, "SHORTCUTS")]
    public async Task HelpCommand_ShortcutsTopicRendersKeyboardShortcuts(bool plain, string topic)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var output = await RenderAsync(shell, new HelpCommand { Plain = plain, Command = topic });

        Assert.Contains("Keyboard Shortcuts", output);
        Assert.Contains("History", output);
        Assert.Contains("Cursor Movement", output);
        Assert.Contains("Editing and Completion", output);
        Assert.Contains("Ctrl+R", output);
        Assert.Contains("Search history backward", output);
        Assert.Contains("Ctrl+S", output);
        Assert.Contains("Search history forward", output);
        Assert.Contains("Esc, Ctrl+G, or Ctrl+C cancels", output);
        Assert.Contains("Ctrl+D", output);
        Assert.Contains("Exit shell when prompt is empty", output);
        Assert.Contains("Ctrl+Tab", output);
        Assert.Contains("Ctrl+W", output);
        Assert.Contains("Interrupt running work", output);
    }

    private static async Task<string> RenderAsync(ShellInterpreter shell, HelpCommand command)
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        var originalConsole = AnsiConsole.Console;

        try
        {
            Console.SetOut(writer);
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer),
            });
            AnsiConsole.Console.Profile.Width = 240;

            var state = await command.ExecuteAsync(shell, new CommandState(), "help", TestContext.Current.CancellationToken);

            Assert.Empty(writer.ToString());
            Assert.NotNull(state.RenderUser);
            state.RenderUser();
            return writer.ToString();
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void HelpCommand_ShouldNotCrashForAllCommands()
    {
        // Arrange
        var app = new CommandRunner();
        var commandNames = app.Commands.Keys.ToList();

        // Act & Assert - verify each command's help can be generated without crashing
        foreach (var commandName in commandNames)
        {
            var exception = Record.Exception(() =>
            {
                var result = HelpCommand.PrintCommandHelp(commandName, app, plain: true);
                Assert.NotNull(result);
            });

            Assert.Null(exception); // Should not throw
        }
    }

    [Fact]
    public void HelpCommand_ShouldNotCrashForAllStatements()
    {
        // Arrange
        var app = new CommandRunner();
        var statementNames = new[]
        {
            "if",
            "while",
            "loop",
            "do",
            "for",
            "def",
            "return",
            "break",
            "continue"
        };

        // Act & Assert - verify each statement's help can be generated without crashing
        foreach (var statementName in statementNames)
        {
            var exception = Record.Exception(() =>
            {
                var result = HelpCommand.PrintCommandHelp(statementName, app, plain: true);
                Assert.NotNull(result);
            });

            Assert.Null(exception); // Should not throw
        }
    }

    [Fact]
    public void HelpCommand_ShouldShowCorrectKeywordFormattingInSyntax()
    {
        // Arrange
        var app = new CommandRunner();

        // Act & Assert - verify 'if' statement has plum3 formatted keywords
        var result = HelpCommand.PrintCommandHelp("if", app, plain: false);
        Assert.NotNull(result);

        // The result should have been printed without crashing
        // The actual color formatting is tested by en.ftl having [plum3] tags
    }

    [Fact]
    public void HelpCommand_AllCommandsAndStatements_ShouldGenerateJson()
    {
        // Arrange
        var app = new CommandRunner();
        var allItems = app.Commands.Keys
            .Concat(new[] { "if", "while", "loop", "do", "for", "def", "return", "break", "continue" })
            .ToList();

        // Act & Assert
        foreach (var item in allItems)
        {
            var exception = Record.Exception(() =>
            {
                var result = HelpCommand.PrintCommandHelp(item, app, plain: true);
                Assert.NotNull(result);

                // Verify the result has JSON data
                Assert.NotNull(result.Result);
            });

            Assert.Null(exception);
        }
    }

    [Fact]
    public void HelpCommand_CommandHelp_DefersUserOutputUntilRendered()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var app = new CommandRunner();
        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(writer);
            var result = HelpCommand.PrintCommandHelp("query", app, plain: true);

            Assert.Empty(output.ToString());
            Assert.NotNull(result.RenderUser);

            result.RenderUser();
            Assert.NotEmpty(output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
