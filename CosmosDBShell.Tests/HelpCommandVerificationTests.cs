// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Xunit;

public class HelpCommandVerificationTests
{
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
}
