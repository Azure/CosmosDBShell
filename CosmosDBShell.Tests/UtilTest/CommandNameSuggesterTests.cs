// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Util;

public class CommandNameSuggesterTests
{
    private static readonly string[] KnownCommands = new[]
    {
        "ls", "cd", "pwd", "query", "connect", "disconnect", "settings",
        "create", "mkitem", "mkdb", "mkcon", "rm", "rmdb", "rmcon",
        "help", "exit", "cls", "echo", "jq", "ftab", "print", "cat",
    };

    [Theory]
    [InlineData("qery", "query")]
    [InlineData("queryy", "query")]
    [InlineData("connet", "connect")]
    [InlineData("conect", "connect")]
    [InlineData("setings", "settings")]
    [InlineData("setttings", "settings")]
    [InlineData("mkitm", "mkitem")]
    [InlineData("LS", "ls")]
    public void Suggest_ReturnsClosestCandidateForCommonTypos(string typed, string expected)
    {
        var suggestion = CommandNameSuggester.Suggest(typed, KnownCommands);

        Assert.Equal(expected, suggestion);
    }

    [Theory]
    [InlineData("xyzzyfoo")]
    [InlineData("completelyunrelated")]
    public void Suggest_ReturnsNullWhenNothingIsCloseEnough(string typed)
    {
        var suggestion = CommandNameSuggester.Suggest(typed, KnownCommands);

        Assert.Null(suggestion);
    }

    [Fact]
    public void Suggest_IgnoresExactMatch()
    {
        // When the user typed something that exactly matches a candidate, the
        // caller has already resolved the command; the suggester should not
        // claim "did you mean 'ls'?" for the literal command "ls".
        var suggestion = CommandNameSuggester.Suggest("ls", KnownCommands);

        Assert.NotEqual("ls", suggestion);
    }

    [Fact]
    public void Suggest_ReturnsNullForEmptyInput()
    {
        Assert.Null(CommandNameSuggester.Suggest(string.Empty, KnownCommands));
    }
}
