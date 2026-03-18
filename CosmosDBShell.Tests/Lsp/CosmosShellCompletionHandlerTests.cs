// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Lsp;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Extensions.Logging.Abstractions;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using CompletionContext = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext;

public class CosmosShellCompletionHandlerTests
{
    private readonly CosmosShellWorkspace workspace;
    private readonly CosmosShellCompletionHandler handler;
    private readonly DocumentUri uri = DocumentUri.From("file:///completion.csh");

    public CosmosShellCompletionHandlerTests()
    {
        this.workspace = new CosmosShellWorkspace();
        this.handler = new CosmosShellCompletionHandler(this.workspace, NullLogger<CosmosShellCompletionHandler>.Instance);
        _ = ShellInterpreter.Instance; // Ensure interpreter is initialized (commands loaded)
    }

    private async Task<CompletionList> GetCompletionsAsync(string content, int line, int column)
    {
        this.workspace.OpenDocument(this.uri, content, 1);
        var param = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = this.uri },
            Position = new Position(line, column),
            Context = new CompletionContext()
        };
        return await this.handler.Handle(param, CancellationToken.None);
    }

    private static HashSet<string> Labels(CompletionList list) => list.Items.Select(i => i.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task CommandPosition_EmptyToken_ReturnsKeywordsAndCommands()
    {
        // Caret at start -> command position
        var completions = await GetCompletionsAsync("", 0, 0);
        var labels = Labels(completions);

        Assert.Contains("if", labels);          // keyword
        Assert.Contains("query", labels);       // known command
        Assert.Contains("connect", labels);     // another command (assuming built-in)
    }

    [Fact]
    public async Task CommandPosition_PartialPrefix_FiltersCommands()
    {
        var completions = await GetCompletionsAsync("qu", 0, 1);
        var labels = Labels(completions);

        Assert.Contains("query", labels);
        // Ensure unrelated keyword not filtered out incorrectly if not matching prefix
        Assert.DoesNotContain("while", labels.Where(l => !l.StartsWith("w", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task VariableCompletion_SuggestsVariables()
    {
        ShellInterpreter.Instance.SetVariable("foo", new ShellText("bar"));
        ShellInterpreter.Instance.SetVariable("foobar", new ShellText("baz"));

        var completions = await GetCompletionsAsync("echo $fo", 0, 8); // position after $fo
        var labels = Labels(completions);

        Assert.Contains("$foo", labels);
        Assert.Contains("$foobar", labels);
    }

    [Fact]
    public async Task VariableCompletion_IgnoresWhenNotVariableContext()
    {
        var completions = await GetCompletionsAsync("echo fo", 0, 7);
        var labels = Labels(completions);

        Assert.DoesNotContain("$foo", labels);
    }

    [Fact]
    public async Task OptionCompletion_SuggestsOptions()
    {
        // 'query' has options like -max, -metrics, -format
        var completions = await GetCompletionsAsync("query -", 0, 6);
        var labels = Labels(completions);

        Assert.Contains("-max", labels);
        Assert.Contains("-metrics", labels);
        Assert.Contains("-format", labels);
    }

    [Fact]
    public async Task OptionCompletion_PartialDashFilters()
    {
        var completions = await GetCompletionsAsync("query -m", 0, 8);
        var labels = Labels(completions);

        Assert.Contains("-max", labels);
        Assert.Contains("-metrics", labels);
        // Should not include unrelated e.g. -format
        Assert.DoesNotContain("-format", labels);
    }

    [Fact]
    public async Task OptionCompletion_AlreadyUsed_Excluded()
    {
        var completions = await GetCompletionsAsync("query -max:10 ", 0, 13);
        var labels = Labels(completions);

        Assert.DoesNotContain("-max", labels);
        Assert.Contains("-metrics", labels);
    }

    [Fact]
    public async Task ParameterCompletion_FirstParameterSuggested()
    {
        // Query command first positional parameter is "query"
        var completions = await GetCompletionsAsync("query ", 0, 6);
        var labels = Labels(completions);

        Assert.Contains("query", labels);
    }

    [Fact]
    public async Task ParameterCompletion_SuppressedWhenOptionValuePending()
    {
        // Provide option that needs a value (-max) and caret right after space: expecting a value not a parameter suggestion
        var completions = await GetCompletionsAsync("query -max ", 0, 11);
        var labels = Labels(completions);

        // The parameter 'query' should not appear because the next token should be the value of -max
        Assert.DoesNotContain("query", labels);
    }

    [Fact]
    public async Task MixedCompletions_CommandPositionIncludesKeywords()
    {
        var completions = await GetCompletionsAsync("wh", 0, 2);
        var labels = Labels(completions);

        Assert.Contains("while", labels);
        Assert.DoesNotContain("for", labels.Where(l => !l.StartsWith("f", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Cache_ReturnsSameListWithinWindow()
    {
        var param = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = this.uri },
            Position = new Position(0, 0),
            Context = new CompletionContext()
        };

        this.workspace.OpenDocument(this.uri, "", 1);
        var first = await this.handler.Handle(param, CancellationToken.None);
        var second = await this.handler.Handle(param, CancellationToken.None);

        // Reference equality (cached CompletionList)
        Assert.True(object.ReferenceEquals(first, second));
    }

    [Fact]
    public async Task AfterAddingVariable_NewVariableNotInCachedResult()
    {
        this.workspace.OpenDocument(this.uri, "echo $", 1);
        var param = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = this.uri },
            Position = new Position(0, 6),
            Context = new CompletionContext()
        };

        var first = await this.handler.Handle(param, CancellationToken.None);

        ShellInterpreter.Instance.SetVariable("newVar", new ShellText("v"));

        var second = await this.handler.Handle(param, CancellationToken.None);

        // Cached result should not include new variable yet
        var firstLabels = Labels(first);
        var secondLabels = Labels(second);
        Assert.True(object.ReferenceEquals(first, second));
        Assert.DoesNotContain("$newVar", firstLabels);
        Assert.DoesNotContain("$newVar", secondLabels);
    }
}