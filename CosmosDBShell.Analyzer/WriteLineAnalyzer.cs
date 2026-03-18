// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace CosmosShell.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class WriteLineAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CZ0002";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly string Title = "WriteLine";
        private static readonly string Description = "Call to Console.WriteLine";
        private const string Category = "Naming";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, Description, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
        }
        private void Analyze(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is InvocationExpressionSyntax invoke))
            {
                return;
            }
            if (context.SemanticModel.GetSymbolInfo(invoke.Expression).Symbol is IMethodSymbol method)
            {
                if (method.IsStatic && method.Name == "WriteLine" && method.ContainingType.Name == "Console")
                {
                    var diagnostic = Diagnostic.Create(Rule, invoke.GetLocation());
                    context.ReportDiagnostic(diagnostic);

                }
            }
        }
    }
}
