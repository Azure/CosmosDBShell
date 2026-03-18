// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class FtabCommandTests
{
    [Fact]
    public async Task ExecuteAsync_RendersItemsWrapperToRedirect_AndPreservesResult()
    {
        var shell = ShellInterpreter.CreateInstance();
        var outputFile = Path.Combine(Path.GetTempPath(), $"ftab-{Guid.NewGuid():N}.txt");
        shell.StdOutRedirect = outputFile;

        var state = new CommandState();
        state.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            items = new[]
            {
                new { id = "1", name = "alpha" },
                new { id = "2", name = "beta" },
            },
        }));

        var command = new FtabCommand();

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        Assert.Same(state, result);
        Assert.True(result.IsPrinted);
        Assert.NotNull(result.Result);

        var text = await File.ReadAllTextAsync(outputFile, CancellationToken.None);
        Assert.Contains("id", text);
        Assert.Contains("name", text);
        Assert.Contains("alpha", text);
        Assert.Contains("beta", text);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesFieldsAndTake_ToDocumentsWrapper()
    {
        var shell = ShellInterpreter.CreateInstance();
        var outputFile = Path.Combine(Path.GetTempPath(), $"ftab-{Guid.NewGuid():N}.txt");
        shell.StdOutRedirect = outputFile;

        var state = new CommandState();
        state.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            documents = new[]
            {
                new { id = "1", name = "alpha", ignored = "first" },
                new { id = "2", name = "beta", ignored = "second" },
            },
        }));

        var command = new FtabCommand
        {
            Fields = "name,id",
            Take = 1,
        };

        var result = await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        Assert.Same(state, result);
        var text = await File.ReadAllTextAsync(outputFile, CancellationToken.None);

        Assert.Contains("name", text);
        Assert.Contains("id", text);
        Assert.Contains("alpha", text);
        Assert.DoesNotContain("beta", text);
        Assert.DoesNotContain("ignored", text);
    }

    [Fact]
    public async Task ExecuteAsync_SortsObjectRows_BeforeTake()
    {
        var shell = ShellInterpreter.CreateInstance();
        var outputFile = Path.Combine(Path.GetTempPath(), $"ftab-{Guid.NewGuid():N}.txt");
        shell.StdOutRedirect = outputFile;

        var state = new CommandState();
        state.Result = new ShellJson(JsonSerializer.SerializeToElement(new[]
        {
            new { id = "2", name = "beta" },
            new { id = "3", name = "charlie" },
            new { id = "1", name = "alpha" },
        }));

        var command = new FtabCommand
        {
            Sort = "name",
            Take = 2,
        };

        await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var text = await File.ReadAllTextAsync(outputFile, CancellationToken.None);
        var alphaIndex = text.IndexOf("alpha", StringComparison.Ordinal);
        var betaIndex = text.IndexOf("beta", StringComparison.Ordinal);
        var charlieIndex = text.IndexOf("charlie", StringComparison.Ordinal);

        Assert.True(alphaIndex >= 0);
        Assert.True(betaIndex > alphaIndex);
        Assert.Equal(-1, charlieIndex);
    }

    [Fact]
    public async Task ExecuteAsync_SortsObjectRows_Descending()
    {
        var shell = ShellInterpreter.CreateInstance();
        var outputFile = Path.Combine(Path.GetTempPath(), $"ftab-{Guid.NewGuid():N}.txt");
        shell.StdOutRedirect = outputFile;

        var state = new CommandState();
        state.Result = new ShellJson(JsonSerializer.SerializeToElement(new[]
        {
            new { id = "1", name = "alpha" },
            new { id = "2", name = "beta" },
            new { id = "3", name = "charlie" },
        }));

        var command = new FtabCommand
        {
            Sort = "name:desc",
        };

        await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var text = await File.ReadAllTextAsync(outputFile, CancellationToken.None);
        var alphaIndex = text.IndexOf("alpha", StringComparison.Ordinal);
        var betaIndex = text.IndexOf("beta", StringComparison.Ordinal);
        var charlieIndex = text.IndexOf("charlie", StringComparison.Ordinal);

        Assert.True(charlieIndex >= 0);
        Assert.True(betaIndex > charlieIndex);
        Assert.True(alphaIndex > betaIndex);
    }

    [Fact]
    public async Task ExecuteAsync_SortsScalarRows_WithValue()
    {
        var shell = ShellInterpreter.CreateInstance();
        var outputFile = Path.Combine(Path.GetTempPath(), $"ftab-{Guid.NewGuid():N}.txt");
        shell.StdOutRedirect = outputFile;

        var state = new CommandState();
        state.Result = new ShellJson(JsonSerializer.SerializeToElement(new[] { 30, 10, 20 }));

        var command = new FtabCommand
        {
            Sort = "value",
        };

        await command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None);

        var text = await File.ReadAllTextAsync(outputFile, CancellationToken.None);
        var tenIndex = text.IndexOf("10", StringComparison.Ordinal);
        var twentyIndex = text.IndexOf("20", StringComparison.Ordinal);
        var thirtyIndex = text.IndexOf("30", StringComparison.Ordinal);

        Assert.True(tenIndex >= 0);
        Assert.True(twentyIndex > tenIndex);
        Assert.True(thirtyIndex > twentyIndex);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsSortField_ForScalarRows()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState();
        state.Result = new ShellJson(JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }));

        var command = new FtabCommand
        {
            Sort = "id",
        };

        var exception = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None));
        Assert.Contains("can only use 'value'", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsFieldSelection_ForScalarRows()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState();
        state.Result = new ShellJson(JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }));

        var command = new FtabCommand
        {
            Fields = "value",
        };

        var exception = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None));
        Assert.Contains("-f option", exception.Message);
    }

    [Fact]
    public void ParseColorizeRules_ParsesSemicolonSeparatedRules()
    {
        var rules = FtabCommand.ParseColorizeRules("type:error:red;status:ok:green");

        Assert.Collection(
            rules,
            rule =>
            {
                Assert.Equal("type", rule.Field);
                Assert.Equal("error", rule.Value);
                Assert.Equal("red", rule.Style);
            },
            rule =>
            {
                Assert.Equal("status", rule.Field);
                Assert.Equal("ok", rule.Value);
                Assert.Equal("green", rule.Style);
            });
    }

    [Fact]
    public void FormatStyledCell_AppliesMatchingRule()
    {
        var rendered = FtabCommand.FormatStyledCell(
            "type",
            "error",
            FtabCommand.ParseColorizeRules("type:error:red"));

        Assert.Equal("[red]error[/]", rendered);
    }

    [Fact]
    public void ParseColorizeRules_RejectsInvalidRule()
    {
        var exception = Assert.Throws<CommandException>(() => FtabCommand.ParseColorizeRules("invalid-rule"));
        Assert.Contains("field:value:style", exception.Message);
    }

    [Fact]
    public void ParseFormat_SupportsMarkdownAndHtml()
    {
        Assert.Equal(FtabCommand.FtabOutputFormat.Markdown, FtabCommand.ParseFormat("markdown"));
        Assert.Equal(FtabCommand.FtabOutputFormat.Html, FtabCommand.ParseFormat("html"));
        Assert.Equal(FtabCommand.FtabOutputFormat.Default, FtabCommand.ParseFormat(null));
    }

    [Fact]
    public void ParseSort_SupportsAscendingAndDescending()
    {
        var ascending = FtabCommand.ParseSort("name");
        var descending = FtabCommand.ParseSort("name:desc");

        Assert.Equal("name", ascending.Field);
        Assert.False(ascending.Descending);
        Assert.Equal("name", descending.Field);
        Assert.True(descending.Descending);
    }

    [Fact]
    public void ParseSort_RejectsInvalidDirection()
    {
        var exception = Assert.Throws<CommandException>(() => FtabCommand.ParseSort("name:sideways"));
        Assert.Contains("asc' or 'desc", exception.Message);
    }

    [Fact]
    public void RenderMarkdown_ProducesPipeTable()
    {
        var markdown = FtabCommand.RenderMarkdown(
            ["id", "name"],
            [["1", "alpha"], ["2", "beta"]]);

        Assert.Contains("| id | name |", markdown);
        Assert.Contains("| --- | --- |", markdown);
        Assert.Contains("| 1 | alpha |", markdown);
        Assert.Contains("| 2 | beta |", markdown);
    }

    [Fact]
    public void RenderHtml_ProducesHtmlTable()
    {
        var html = FtabCommand.RenderHtml(
            ["id", "payload"],
            [["1", "{\n  \"name\": \"alpha\"\n}"]]);

        Assert.Contains("<table", html);
        Assert.Contains("<th>id</th>", html);
        Assert.Contains("<th>payload</th>", html);
        Assert.Contains("<td><pre style=\"margin:0\">1</pre></td>", html);
        Assert.Contains("&quot;alpha&quot;", html);
        Assert.Contains("<br/>", html);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsColorize_ForNonDefaultFormats()
    {
        var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState();
        state.Result = new ShellJson(JsonSerializer.SerializeToElement(new[] { new { type = "error" } }));

        var command = new FtabCommand
        {
            Format = "markdown",
            Colorize = "type:error:red",
        };

        var exception = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, state, string.Empty, CancellationToken.None));
        Assert.Contains("only supported with -format default", exception.Message);
    }
}