// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Collections.Immutable;
using CosmosShell.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

public class DirectConsoleOutputAnalyzerTests
{
    [Theory]
    [InlineData("Console.Clear();", true)]
    [InlineData("Console.Write(\"text\");", true)]
    [InlineData("Console.WriteLine(\"text\");", true)]
    [InlineData("Console.Out.WriteLine(\"text\");", true)]
    [InlineData("Console.Error.WriteLine(\"text\");", true)]
    [InlineData("Console.ReadLine();", false)]
    [InlineData("var width = Console.WindowWidth;", false)]
    [InlineData("using var writer = new System.IO.StringWriter(); writer.WriteLine(\"text\");", false)]
    public async Task DirectConsoleOperations_EnforceOutputBoundary(string statement, bool reportsDiagnostic)
    {
        var diagnostics = await AnalyzeAsync("Example", statement);

        if (reportsDiagnostic)
        {
            Assert.Equal(DirectConsoleOutputAnalyzer.DiagnosticId, Assert.Single(diagnostics).Id);
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [InlineData("Console.Clear();")]
    [InlineData("Console.WriteLine(\"text\");")]
    public async Task ShellOutput_CanAccessConsole(string statement)
    {
        Assert.Empty(await AnalyzeAsync("Azure.Data.Cosmos.Shell.Core.ShellOutput", statement));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string typeName, string statement)
    {
        var separator = typeName.LastIndexOf('.');
        var namespaceName = separator < 0 ? "ExampleNamespace" : typeName[..separator];
        var className = typeName[(separator + 1)..];
        var source = $$"""
            using System;
            namespace {{namespaceName}};
            internal class {{className}}
            {
                internal void Run()
                {
                    {{statement}}
                }
            }
            """;
        var assemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Runtime assembly references are unavailable.");
        var references = assemblies.Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return await compilation.WithAnalyzers([new DirectConsoleOutputAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
