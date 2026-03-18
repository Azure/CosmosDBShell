// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace CosmosShell.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class EmptyCatchClauseAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CZ0001";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly string Title = "Empty Catch Clause";
        private static readonly string Description = "Empty catch clauses are not allowed";
        private const string Category = "Naming";
        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, Description, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.CatchClause);
        }
        private void Analyze(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is CatchClauseSyntax cd))
            {
                return;
            }
            if (cd.Block.Statements.Count == 0)
            {
                if (cd.Declaration?.Type != null && !(context.SemanticModel.GetTypeInfo(cd.Declaration?.Type).Type?.Name == "Exception"))
                {
                    return;
                }
                var diagnostic = Diagnostic.Create(Rule, cd.CatchKeyword.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
