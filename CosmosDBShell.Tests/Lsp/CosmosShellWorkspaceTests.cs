// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Lsp;

using System.Linq;

using Azure.Data.Cosmos.Shell.Lsp;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using Xunit;

public class CosmosShellWorkspaceTests
{
    private readonly CosmosShellWorkspace workspace = new();
    private readonly DocumentUri uri = DocumentUri.From("file:///ws.csh");

    [Theory]
    [InlineData("return")]
    [InlineData("if true { return }")]
    [InlineData("def empty { return }")]
    [InlineData("def empty { if true { return } }")]
    public void BareReturn_AtStatementBoundary_IsValid(string source)
    {
        this.workspace.OpenDocument(this.uri, source, 1);
        var document = this.workspace.GetDocument(this.uri)!;
        Assert.True(document.LastParseResult!.Success);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("def identity [value] { return $value }; identity 1")]
    [InlineData("def identity [value] { return $value }; $result = (identity 1)")]
    [InlineData("def first { return (second) }; def second { return 1 }; first")]
    [InlineData("def recursive { recursive }")]
    [InlineData("def outer { def inner { return 1 }; inner }; outer")]
    public void LocalFunctionCalls_AreResolvedWithoutUnknownCommandDiagnostics(string source)
    {
        this.workspace.OpenDocument(this.uri, source, 1);
        var document = this.workspace.GetDocument(this.uri)!;
        Assert.True(document.LastParseResult!.Success);
        Assert.Empty(document.Diagnostics);
        Assert.Contains(document.SemanticModel!.Symbols, symbol => symbol is Azure.Data.Cosmos.Shell.Lsp.Semantics.FunctionSymbol);
        Assert.Contains(document.SemanticModel.References, reference => reference.Symbol is Azure.Data.Cosmos.Shell.Lsp.Semantics.FunctionSymbol && !reference.IsDefinition);
    }

    [Theory]
    [InlineData("repeat", "repeat")]
    [InlineData("$first = (repeat)", "$second = (repeat)")]
    public void FunctionRedefinition_BindsCallsToLatestPrecedingDefinition(string firstCall, string secondCall)
    {
        var source = $"def repeat {{ return 1 }}; {firstCall}; def repeat {{ return 2 }}; {secondCall}";
        this.workspace.OpenDocument(this.uri, source, 1);
        var document = this.workspace.GetDocument(this.uri)!;
        Assert.Empty(document.Diagnostics);
        var model = document.SemanticModel!;
        var functions = model.Symbols.OfType<Azure.Data.Cosmos.Shell.Lsp.Semantics.FunctionSymbol>().OrderBy(symbol => symbol.Start).ToArray();
        Assert.Equal(2, functions.Length);
        var calls = model.References.Where(reference => !reference.IsDefinition && reference.Symbol is Azure.Data.Cosmos.Shell.Lsp.Semantics.FunctionSymbol).OrderBy(reference => reference.Start).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Same(functions[0], calls[0].Symbol);
        Assert.Same(functions[1], calls[1].Symbol);
        Assert.Same(functions[1], model.GetSymbolAt(calls[1].Start + 1));
        foreach (var function in functions)
        {
            Assert.Single(model.FindReferences(function), reference => reference.IsDefinition);
            Assert.Single(model.FindReferences(function), reference => !reference.IsDefinition);
        }
    }

    [Theory]
    [InlineData("def example { missing_command_xyz }")]
    [InlineData("if true { missing_command_xyz }")]
    [InlineData("if false {} else { missing_command_xyz }")]
    [InlineData("while true { missing_command_xyz }")]
    [InlineData("do { missing_command_xyz } while false")]
    [InlineData("for $item in [1] { missing_command_xyz }")]
    [InlineData("loop { missing_command_xyz }")]
    [InlineData("echo 1 | missing_command_xyz")]
    [InlineData("$result = (missing_command_xyz)")]
    [InlineData("def example { return (missing_command_xyz) }")]
    [InlineData("if (missing_command_xyz) {}")]
    [InlineData("exec (missing_command_xyz)")]
    public void NestedCommands_AreAnalyzedAtTheirSourcePosition(string source)
    {
        this.workspace.OpenDocument(this.uri, source, 1);
        var diagnostic = Assert.Single(this.workspace.GetDocument(this.uri)!.Diagnostics);
        Assert.Equal("Unknown command 'missing_command_xyz'.", diagnostic.Message);
        var start = source.IndexOf("missing_command_xyz", System.StringComparison.Ordinal);
        Assert.Equal(new Position(0, start), diagnostic.Range.Start);
        Assert.Equal(new Position(0, start + "missing_command_xyz".Length), diagnostic.Range.End);
    }

    [Theory]
    [InlineData("def example { ls --not_an_option_xyz }")]
    [InlineData("while true { ls --not_an_option_xyz }")]
    [InlineData("$value = (ls --not_an_option_xyz)")]
    public void NestedBuiltinOptions_StillReceiveValidation(string source)
    {
        this.workspace.OpenDocument(this.uri, source, 1);
        Assert.Contains(this.workspace.GetDocument(this.uri)!.Diagnostics, diagnostic => diagnostic.Message.Contains("Unknown option '-not_an_option_xyz'"));
    }

    [Fact]
    public void RemovingFunctionDefinition_ClearsSymbolsAndReportsUnresolvedCall()
    {
        this.workspace.OpenDocument(this.uri, "def local_function_xyz { return 1 }; local_function_xyz", 1);
        var document = this.workspace.GetDocument(this.uri)!;
        var model = document.SemanticModel!;
        var symbol = Assert.Single(model.Symbols.OfType<Azure.Data.Cosmos.Shell.Lsp.Semantics.FunctionSymbol>());
        var references = model.References.Where(reference => ReferenceEquals(reference.Symbol, symbol)).ToArray();
        Assert.Equal(2, references.Length);
        Assert.Single(references, reference => reference.IsDefinition);
        Assert.Single(references, reference => !reference.IsDefinition);

        this.workspace.UpdateDocument(this.uri, "local_function_xyz", 2);
        Assert.Empty(document.SemanticModel!.Symbols.OfType<Azure.Data.Cosmos.Shell.Lsp.Semantics.FunctionSymbol>());
        Assert.Equal("Unknown command 'local_function_xyz'.", Assert.Single(document.Diagnostics).Message);
        Assert.Single(model.Symbols.OfType<Azure.Data.Cosmos.Shell.Lsp.Semantics.FunctionSymbol>());
    }

    [Fact]
    public void FunctionNames_RemainCaseSensitive()
    {
        this.workspace.OpenDocument(this.uri, "def lower_function_xyz {} ; LOWER_FUNCTION_XYZ", 1);
        Assert.Equal("Unknown command 'LOWER_FUNCTION_XYZ'.", Assert.Single(this.workspace.GetDocument(this.uri)!.Diagnostics).Message);
    }

    [Theory]
    [InlineData("value", "Value")]
    [InlineData("VALUE", "value")]
    [InlineData("item", "ITEM")]
    public void VariableReferences_KeepCaseDistinctDefinitions(string first, string second)
    {
        var source = $"${first} = 1; ${second} = 2; echo ${second}; echo ${first}";
        this.workspace.OpenDocument(this.uri, source, 1);
        var document = this.workspace.GetDocument(this.uri)!;
        Assert.Empty(document.Diagnostics);
        var model = document.SemanticModel!;
        var variables = model.Symbols.OfType<Azure.Data.Cosmos.Shell.Lsp.Semantics.VariableSymbol>().ToArray();
        Assert.Equal(2, variables.Length);
        foreach (var name in new[] { first, second })
        {
            var symbol = Assert.Single(variables, variable => variable.Name == name);
            var references = model.FindReferences(symbol).ToArray();
            Assert.Equal(2, references.Length);
            var definition = Assert.Single(references, reference => reference.IsDefinition);
            var usage = Assert.Single(references, reference => !reference.IsDefinition);
            Assert.Equal(source.IndexOf("$" + name, System.StringComparison.Ordinal), definition.Start);
            Assert.Equal(source.LastIndexOf("$" + name, System.StringComparison.Ordinal), usage.Start);
            Assert.Same(symbol, model.GetSymbolAt(usage.Start + 1));
        }
    }

    [Theory]
    [InlineData("$value = $value + 1", 2)]
    [InlineData("$value = 0; $value = $value + 1", 3)]
    public void SelfReferentialAssignment_DefinitionRemainsAtFirstAssignmentTarget(string source, int occurrenceCount)
    {
        this.workspace.OpenDocument(this.uri, source, 1);
        var document = this.workspace.GetDocument(this.uri)!;
        Assert.Empty(document.Diagnostics);
        var model = document.SemanticModel!;
        var symbol = Assert.Single(model.Symbols.OfType<Azure.Data.Cosmos.Shell.Lsp.Semantics.VariableSymbol>());
        var references = model.FindReferences(symbol).ToArray();
        Assert.Equal(occurrenceCount, references.Length);
        var definition = Assert.Single(references, reference => reference.IsDefinition);
        Assert.Equal(0, definition.Start);
        Assert.Equal("$value".Length, definition.Length);
        Assert.Equal(0, symbol.Start);
        var usage = Assert.Single(references, reference => reference.Start == source.LastIndexOf("$value", System.StringComparison.Ordinal));
        Assert.False(usage.IsDefinition);
        Assert.Same(symbol, model.GetSymbolAt(usage.Start + 1));
    }

    [Fact]
    public void OpenDocument_StoresAndParses()
    {
        this.workspace.OpenDocument(this.uri, "echo hello", 3);

        var doc = this.workspace.GetDocument(this.uri);
        Assert.NotNull(doc);
        Assert.Equal("echo hello", doc!.Content);
        Assert.Equal(3, doc.Version);
        Assert.NotNull(doc.LastParseResult);
        Assert.True(doc.LastParseResult!.Success);
    }

    [Fact]
    public void GetDocument_Unknown_ReturnsNull()
    {
        Assert.Null(this.workspace.GetDocument(DocumentUri.From("file:///missing.csh")));
    }

    [Fact]
    public void Documents_ReflectsOpenSet()
    {
        var other = DocumentUri.From("file:///other.csh");
        this.workspace.OpenDocument(this.uri, "echo a", 1);
        this.workspace.OpenDocument(other, "echo b", 1);

        Assert.Equal(2, this.workspace.Documents.Count());
    }

    [Fact]
    public void UpdateDocument_Existing_UpdatesContentAndVersion()
    {
        this.workspace.OpenDocument(this.uri, "echo a", 1);
        this.workspace.UpdateDocument(this.uri, "echo b", 2);

        var doc = this.workspace.GetDocument(this.uri);
        Assert.Equal("echo b", doc!.Content);
        Assert.Equal(2, doc.Version);
    }

    [Fact]
    public void UpdateDocument_Missing_OpensDocument()
    {
        this.workspace.UpdateDocument(this.uri, "echo new", 5);

        var doc = this.workspace.GetDocument(this.uri);
        Assert.NotNull(doc);
        Assert.Equal("echo new", doc!.Content);
        Assert.Equal(5, doc.Version);
    }

    [Fact]
    public void CloseDocument_RemovesIt()
    {
        this.workspace.OpenDocument(this.uri, "echo a", 1);
        this.workspace.CloseDocument(this.uri);

        Assert.Null(this.workspace.GetDocument(this.uri));
        Assert.Empty(this.workspace.Documents);
    }

    [Fact]
    public void GetParseResult_ReturnsDocumentResult()
    {
        this.workspace.OpenDocument(this.uri, "echo a", 1);

        var result = this.workspace.GetParseResult(this.uri);
        Assert.NotNull(result);
        Assert.True(result!.Success);
    }

    [Fact]
    public void GetParseResult_Unknown_ReturnsNull()
    {
        Assert.Null(this.workspace.GetParseResult(this.uri));
    }

    [Fact]
    public void GetWordAtPosition_Unknown_ReturnsNull()
    {
        Assert.Null(this.workspace.GetWordAtPosition(this.uri, new Position(0, 0)));
    }

    [Fact]
    public void GetWordAtPosition_ReturnsWord()
    {
        this.workspace.OpenDocument(this.uri, "echo $foo", 1);

        var word = this.workspace.GetWordAtPosition(this.uri, new Position(0, 6));
        Assert.Equal("$foo", word);
    }

    [Fact]
    public void GetWordAtPosition_LineOutOfRange_ReturnsNull()
    {
        this.workspace.OpenDocument(this.uri, "echo a", 1);

        Assert.Null(this.workspace.GetWordAtPosition(this.uri, new Position(10, 0)));
    }

    [Fact]
    public void GetWordAtPosition_CharOutOfRange_ReturnsNull()
    {
        this.workspace.OpenDocument(this.uri, "echo", 1);

        Assert.Null(this.workspace.GetWordAtPosition(this.uri, new Position(0, 20)));
    }

    [Fact]
    public void GetWordAtPosition_AtWordEnd_ReturnsPrecedingWord()
    {
        this.workspace.OpenDocument(this.uri, "a b", 1);

        Assert.Equal("a", this.workspace.GetWordAtPosition(this.uri, new Position(0, 1)));
    }

    [Fact]
    public void GetCompletionContext_Unknown_ReturnsEmpty()
    {
        var context = this.workspace.GetCompletionContext(this.uri, new Position(0, 0));
        Assert.Same(Azure.Data.Cosmos.Shell.Lsp.CompletionContext.Empty, context);
    }

    [Fact]
    public void GetCompletionContext_ReturnsContextWithText()
    {
        this.workspace.OpenDocument(this.uri, "echo hello", 1);

        var context = this.workspace.GetCompletionContext(this.uri, new Position(0, 4));
        Assert.Equal("echo", context.TextUpToPosition);
        Assert.NotNull(context.Document);
    }

    [Fact]
    public void OpenDocument_WithParseError_ProducesDiagnostics()
    {
        // Unterminated string should yield lexer errors -> diagnostics.
        this.workspace.OpenDocument(this.uri, "echo \"unterminated", 1);

        var doc = this.workspace.GetDocument(this.uri);
        Assert.NotNull(doc);
        Assert.NotEmpty(doc!.Diagnostics);
    }

    [Theory]
    [InlineData("break")]
    [InlineData("continue")]
    [InlineData("def duplicate [value value] { return $value }")]
    [InlineData("loop { def invalid { break } }")]
    public void RuntimeSemanticErrors_AreReportedByLsp(string source)
    {
        this.workspace.OpenDocument(this.uri, source, 1);
        var document = this.workspace.GetDocument(this.uri)!;
        var expected = Azure.Data.Cosmos.Shell.Parser.StatementParser.ScriptParseResult.Parse(source, allowReturn: true);
        Assert.False(document.LastParseResult!.Success);
        foreach (var error in expected.Errors)
        {
            Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Message == error.Message);
        }
    }

    [Fact]
    public void SemanticRange_UsesExclusiveEnd_AndClearsAfterFix()
    {
        this.workspace.OpenDocument(this.uri, "\r\nbreak", 1);
        var document = this.workspace.GetDocument(this.uri)!;
        var diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(new Position(1, 0), diagnostic.Range.Start);
        Assert.Equal(new Position(1, 5), diagnostic.Range.End);
        this.workspace.UpdateDocument(this.uri, "loop { break }\nreturn 1", 2);
        Assert.True(document.LastParseResult!.Success);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void DeepExpression_IsRejectedBeforeSemanticAnalysis()
    {
        this.workspace.OpenDocument(this.uri, "$value = " + string.Join(" + ", Enumerable.Repeat("1", 10001)), 1);
        var document = this.workspace.GetDocument(this.uri)!;
        Assert.False(document.LastParseResult!.Success);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Message.Contains("expression tree depth"));
        this.workspace.UpdateDocument(this.uri, "$value = 1", 2);
        Assert.True(document.LastParseResult!.Success);
        Assert.Empty(document.Diagnostics);
    }
}
