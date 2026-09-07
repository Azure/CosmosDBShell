// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp.Semantics;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Performs semantic analysis on parsed AST statements to produce a semantic model containing
/// symbol information, references, and diagnostics for LSP features like hover, go-to-definition,
/// and error highlighting.
/// </summary>
/// <remarks>
/// The analyzer performs the following tasks:
/// - Symbol discovery and registration (commands, variables, functions)
/// - Reference tracking for all symbols with definition/usage distinction
/// - Semantic validation including:
///   - Unknown command detection
///   - Invalid command options
///   - Duplicate option usage
///   - Type mismatches for boolean options
/// - Building a complete semantic model for LSP consumption.
/// </remarks>
public sealed class SemanticAnalyzer
{
    private readonly List<Symbol> symbols = new();
    private readonly List<ReferenceInfo> references = new();
    private readonly List<SemanticDiagnostic> diagnostics = new();
    private readonly HashSet<string> declaredVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionSymbol> functions = new(StringComparer.Ordinal);
    private readonly Dictionary<DefStatement, FunctionSymbol> functionDefinitions = new();

    /// <summary>
    /// Analyzes a collection of parsed statements to produce a semantic model.
    /// </summary>
    /// <param name="statements">The parsed AST statements to analyze.</param>
    /// <param name="source">The source code text (currently unused but reserved for future features).</param>
    /// <returns>A <see cref="SemanticModel"/> containing symbols, references, and diagnostics.</returns>
    public SemanticModel Analyze(IEnumerable<Statement> statements, string source)
    {
        this.symbols.Clear();
        this.references.Clear();
        this.diagnostics.Clear();
        this.declaredVariables.Clear();
        this.functions.Clear();
        this.functionDefinitions.Clear();
        var statementList = statements.ToList();
        foreach (var statement in statementList)
        {
            this.CollectFunctions(statement);
        }

        foreach (var st in statementList)
        {
            this.VisitStatement(st);
        }

        return new SemanticModel
        {
            Symbols = this.symbols.ToArray(),
            References = this.references.ToArray(),
            Diagnostics = this.diagnostics.ToArray(),
        };
    }

    private static IEnumerable<Statement> GetChildStatements(Statement statement)
    {
        return statement switch
        {
            BlockStatement block => block.Statements,
            PipeStatement pipe => pipe.Statements,
            DefStatement function => [function.Statement],
            IfStatement conditional when conditional.ElseStatement != null => [conditional.Statement, conditional.ElseStatement],
            IfStatement conditional => [conditional.Statement],
            ForStatement forLoop => [forLoop.Statement],
            WhileStatement whileLoop => [whileLoop.Statement],
            DoWhileStatement doLoop => [doLoop.Statement],
            LoopStatement loop => [loop.Statement],
            _ => [],
        };
    }

    private void CollectFunctions(Statement statement)
    {
        if (statement is DefStatement definition)
        {
            var symbol = new FunctionSymbol(definition.Name, definition.NameToken.Start, definition.NameToken.Length);
            this.functionDefinitions[definition] = symbol;
            this.functions.TryAdd(definition.Name, symbol);
            this.symbols.Add(symbol);
        }

        foreach (var child in GetChildStatements(statement))
        {
            this.CollectFunctions(child);
        }
    }

    /// <summary>
    /// Visits a statement node in the AST to extract semantic information.
    /// </summary>
    /// <param name="st">The statement to analyze.</param>
    /// <remarks>
    /// Visits expressions and child statements without executing declarations or commands.
    /// </remarks>
    private void VisitStatement(Statement st)
    {
        switch (st)
        {
            case CommandStatement command:
                this.VisitCommand(command);
                break;
            case DefStatement definition:
                this.functions[definition.Name] = this.functionDefinitions[definition];
                this.references.Add(new ReferenceInfo
                {
                    Symbol = this.functionDefinitions[definition],
                    Start = definition.NameToken.Start,
                    Length = definition.NameToken.Length,
                    IsDefinition = true,
                });
                break;
            case AssignmentStatement assignment:
                this.RecordVariableReference(assignment.Variable);
                this.VisitExpression(assignment.Value);
                break;
            case ReturnStatement returned when returned.Value != null:
                this.VisitExpression(returned.Value);
                break;
            case IfStatement conditional:
                this.VisitExpression(conditional.Condition);
                break;
            case WhileStatement whileLoop:
                this.VisitExpression(whileLoop.Condition);
                break;
            case DoWhileStatement doLoop:
                this.VisitExpression(doLoop.Condition);
                break;
            case ForStatement forLoop:
                this.VisitExpression(forLoop.Collection);
                break;
            case ExecStatement executed:
                this.VisitExpression(executed.CommandExpression);
                foreach (var argument in executed.Arguments)
                {
                    this.VisitExpression(argument);
                }

                break;
        }

        foreach (var child in GetChildStatements(st))
        {
            this.VisitStatement(child);
        }
    }

    private void VisitCommand(CommandStatement cmd)
    {
        var name = cmd.Name ?? string.Empty;
        if (name.Length > 0)
        {
            if (this.functions.TryGetValue(name, out var function))
            {
                this.references.Add(new ReferenceInfo
                {
                    Symbol = function,
                    Start = cmd.Start,
                    Length = cmd.CommandToken.Length,
                    IsDefinition = false,
                });
            }
            else
            {
                var sym = new CommandSymbol(name, cmd.Start, Math.Max(1, name.Length));
                this.symbols.Add(sym);
                this.references.Add(new ReferenceInfo
                {
                    Symbol = sym,
                    Start = cmd.Start,
                    Length = Math.Max(1, name.Length),
                    IsDefinition = true,
                });

                if (!ShellInterpreter.Instance.App.Commands.TryGetValue(name, out var factory))
                {
                    this.diagnostics.Add(new SemanticDiagnostic
                    {
                        Code = "SEM001",
                        Message = $"Unknown command '{name}'.",
                        Start = cmd.Start,
                        Length = Math.Max(1, name.Length),
                        Severity = SemanticDiagnosticSeverity.Error,
                    });
                }
                else
                {
                    this.ValidateCommandOptions(cmd, factory);
                }
            }
        }

        foreach (var arg in cmd.Arguments)
        {
            this.VisitExpression(arg);
        }
    }

    /// <summary>
    /// Validates command options against the command factory definition.
    /// </summary>
    /// <param name="cmd">The command statement containing options to validate.</param>
    /// <param name="factory">The command factory containing valid option definitions.</param>
    /// <remarks>
    /// Produces diagnostics for:
    /// - Unknown options (SEM002)
    /// - Duplicate options (SEM003)
    /// - Boolean options with non-boolean values (SEM004)
    /// Special handling for built-in options: help, '?'.
    /// </remarks>
    private void ValidateCommandOptions(CommandStatement cmd, CommandFactory factory)
    {
        // Collect valid option names (case-insensitive) including all aliases
        var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var opt in factory.AllOptions)
        {
            foreach (var n in opt.Name)
            {
                valid.Add(n);
            }
        }

        // Always allow help / ? as special
        valid.Add("help");
        valid.Add("?");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var optExpr in cmd.Arguments.OfType<CommandOption>())
        {
            var optName = optExpr.Name;

            // Duplicate detection
            if (!seen.Add(optName))
            {
                this.diagnostics.Add(new SemanticDiagnostic
                {
                    Code = "SEM003",
                    Message = $"Duplicate option '-{optName}'.",
                    Start = optExpr.Start,
                    Length = optExpr.Length,
                    Severity = SemanticDiagnosticSeverity.Warning,
                });
                continue;
            }

            // Validity check
            if (!valid.Contains(optName))
            {
                this.diagnostics.Add(new SemanticDiagnostic
                {
                    Code = "SEM002",
                    Message = $"Unknown option '-{optName}' for command '{cmd.Name}'.",
                    Start = optExpr.Start,
                    Length = optExpr.Length,
                    Severity = SemanticDiagnosticSeverity.Error,
                });
                continue;
            }

            // Type/value validation (only if option belongs to factory; skip help/?)
            if (optName.Equals("help", StringComparison.OrdinalIgnoreCase) || optName == "?")
            {
                continue;
            }

            var optDef = factory.AllOptions.FirstOrDefault(o => o.MatchesArgument(optName));
            if (optDef != null)
            {
                if (optDef.IsBool && optExpr.Value != null)
                {
                    // Boolean options should not have an explicit value (semantic style guidance)
                    var valText = optExpr.Value.ToString() ?? string.Empty;
                    if (!valText.Equals("true", StringComparison.OrdinalIgnoreCase) &&
                        !valText.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        this.diagnostics.Add(new SemanticDiagnostic
                        {
                            Code = "SEM004",
                            Message = $"Boolean option '-{optName}' has non-boolean value '{valText}'.",
                            Start = optExpr.Start,
                            Length = optExpr.Length,
                            Severity = SemanticDiagnosticSeverity.Warning,
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Recursively visits expression nodes to extract semantic information.
    /// </summary>
    /// <param name="expr">The expression to analyze.</param>
    /// <remarks>
    /// Handles various expression types including:
    /// - Variables: Records references and tracks declarations
    /// - Binary/Unary operators: Recursively visits operands
    /// - JSON objects/arrays: Visits nested expressions
    /// - Interpolated strings: Analyzes embedded expressions.
    /// </remarks>
    private void VisitExpression(Expression expr)
    {
        switch (expr)
        {
            case VariableExpression ve:
                this.RecordVariableReference(ve);
                break;
            case BinaryOperatorExpression be:
                this.VisitExpression(be.Left);
                this.VisitExpression(be.Right);
                break;
            case UnaryOperatorExpression ue:
                this.VisitExpression(ue.Expression);
                break;
            case CommandExpression command:
                var statement = new CommandStatement(command.CommandToken);
                statement.Arguments.AddRange(command.Arguments);
                this.VisitCommand(statement);
                break;
            case CommandOption option when option.Value != null:
                this.VisitExpression(option.Value);
                break;
            case FilterPipeExpression pipe:
                this.VisitExpression(pipe.Left);
                this.VisitExpression(pipe.Right);
                break;
            case FilterCallExpression call:
                foreach (var argument in call.Arguments)
                {
                    this.VisitExpression(argument);
                }

                break;
            case ParensExpression pe:
                this.VisitExpression(pe.InnerExpression);
                break;
            case JsonExpression je:
                foreach (var kv in je.Properties)
                {
                    this.VisitExpression(kv.Value);
                }

                break;
            case JsonArrayExpression ja:
                foreach (var e in ja.Expressions)
                {
                    this.VisitExpression(e);
                }

                break;
            case InterpolatedStringExpression ise:
                foreach (var e in ise.Expressions)
                {
                    this.VisitExpression(e);
                }

                break;
            case ErrorExpression:
                // Skip error expressions as they're handled by parser diagnostics
                break;
        }
    }

    /// <summary>
    /// Records a variable reference and tracks variable declarations.
    /// </summary>
    /// <param name="ve">The variable expression to record.</param>
    /// <remarks>
    /// The first occurrence of a variable is treated as its definition.
    /// Subsequent occurrences are recorded as references to that definition.
    /// This simplistic approach works well for scripts without explicit
    /// variable declaration statements.
    /// </remarks>
    private void RecordVariableReference(VariableExpression ve)
    {
        var name = ve.Name;
        if (!this.declaredVariables.Contains(name))
        {
            // First occurrence - treat as definition
            var sym = new VariableSymbol(name, ve.Start, ve.Length);
            this.symbols.Add(sym);
            this.declaredVariables.Add(name);

            this.references.Add(new ReferenceInfo
            {
                Symbol = sym,
                Start = ve.Start,
                Length = ve.Length,
                IsDefinition = true,
            });
        }
        else
        {
            // Subsequent occurrence - reference to existing symbol
            var sym = this.symbols.OfType<VariableSymbol>().First(v => v.Name.Equals(name, StringComparison.Ordinal));
            this.references.Add(new ReferenceInfo
            {
                Symbol = sym,
                Start = ve.Start,
                Length = ve.Length,
                IsDefinition = false,
            });
        }
    }
}