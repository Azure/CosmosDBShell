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
    /// <summary>
    /// Requires user-facing console output to go through ShellOutput, which applies the
    /// quiet, machine-mode, and MCP stdio presentation policies. Direct writes bypass those
    /// policies and can corrupt structured stdout or the MCP protocol stream.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class DirectConsoleOutputAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CZ0003";

        private const string Category = "Usage";
        private const string AllowedType = "Azure.Data.Cosmos.Shell.Core.ShellOutput";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Direct console output",
            "'{0}' bypasses the shell output policy; write through ShellOutput instead",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "User-facing output must go through ShellOutput so --quiet, machine mode, and MCP stdio are honored.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
        }

        private static void Analyze(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method))
            {
                return;
            }

            if (!IsDirectOutput(context.SemanticModel, invocation, method, context))
            {
                return;
            }

            var containingType = context.ContainingSymbol?.ContainingType;
            while (containingType != null)
            {
                if (containingType.ToDisplayString() == AllowedType)
                {
                    return;
                }

                containingType = containingType.ContainingType;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), method.ContainingType.Name + "." + method.Name));
        }

        private static bool IsDirectOutput(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method, SyntaxNodeAnalysisContext context)
        {
            var type = method.ContainingType?.ToDisplayString();

            // Console.Write / Console.WriteLine.
            if (method.IsStatic && type == "System.Console" && method.Name.StartsWith("Write"))
            {
                return true;
            }

            // Every static AnsiConsole call except Create renders to the global console.
            if (method.IsStatic && type == "Spectre.Console.AnsiConsole" && method.Name != "Create")
            {
                return true;
            }

            // Console.Out.Write*, Console.Error.Write*, and AnsiConsole.Console.<extension>.
            if (invocation.Expression is MemberAccessExpressionSyntax access
                && model.GetSymbolInfo(access.Expression, context.CancellationToken).Symbol is IPropertySymbol receiver
                && receiver.IsStatic)
            {
                var owner = receiver.ContainingType?.ToDisplayString();
                if (owner == "System.Console" && (receiver.Name == "Out" || receiver.Name == "Error") && method.Name.StartsWith("Write"))
                {
                    return true;
                }

                if (owner == "Spectre.Console.AnsiConsole" && receiver.Name == "Console")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
