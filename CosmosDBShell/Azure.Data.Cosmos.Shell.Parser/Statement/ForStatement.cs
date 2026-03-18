// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Represents a for-in loop statement that iterates over elements in a collection.
/// </summary>
/// <remarks>
/// The for statement iterates over each element in an array or collection, assigning
/// each element to a loop variable and executing the loop body.
///
/// Syntax:
/// for $variable in collection
///     statement
///
/// Example:
/// for $item in [1, 2, 3, 4, 5]
///     echo "Number: $item"
///
/// The collection must evaluate to a JSON array. The loop variable is created in the
/// current scope and persists after the loop completes.
/// </remarks>
[AstHelp("statement-for")]
internal class ForStatement : Statement
{
    public ForStatement(Token forToken, Token variableToken, Token inToken, Expression collection, Statement statement)
    {
        this.ForToken = forToken ?? throw new ArgumentNullException(nameof(forToken));
        this.InToken = inToken ?? throw new ArgumentNullException(nameof(inToken));
        this.VariableToken = variableToken ?? throw new ArgumentNullException(nameof(variableToken));
        this.Collection = collection ?? throw new ArgumentNullException(nameof(collection));
        this.Statement = statement ?? throw new ArgumentNullException(nameof(statement));
    }

    /// <summary>
    /// Gets the 'for' keyword token.
    /// </summary>
    public Token ForToken { get; }

    /// <summary>
    /// Gets the loop variable token (including the $ prefix).
    /// </summary>
    public Token VariableToken { get; }

    /// <summary>
    /// Gets the loop variable name (without the $ prefix).
    /// </summary>
    public string VariableName { get => this.VariableToken.Value[1..]; }

    /// <summary>
    /// Gets the 'in' keyword token.
    /// </summary>
    public Token InToken { get; }

    /// <summary>
    /// Gets the collection expression to iterate over.
    /// </summary>
    public Expression Collection { get; }

    /// <summary>
    /// Gets the statement body executed for each iteration.
    /// </summary>
    public Statement Statement { get; }

    /// <summary>
    /// Gets the starting position of the for statement in the source text.
    /// </summary>
    public override int Start => this.ForToken.Start;

    /// <summary>
    /// Gets the total length of the for statement in the source text.
    /// </summary>
    public override int Length => (this.Statement.Start + this.Statement.Length) - this.ForToken.Start;

    /// <summary>
    /// Executes the for loop, iterating over each element in the collection.
    /// </summary>
    /// <remarks>
    /// The loop:
    /// - Evaluates the collection to a JSON array
    /// - Assigns each element to the loop variable
    /// - Executes the statement body for each element
    /// - Handles break statements to exit early
    /// - Preserves the loop variable after completion.
    /// </remarks>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var evaluatedCollection = await this.Collection.EvaluateAsync(shell, commandState, token);
        var collectionResult = evaluatedCollection.ConvertShellObject(DataType.Json);
        if (collectionResult is not JsonElement collection)
        {
            throw new InvalidOperationException("Collection must evaluate to a JSON array");
        }

        foreach (var arr in collection.EnumerateArray())
        {
            ShellObject elementValue = arr.ValueKind switch
            {
                JsonValueKind.Number => new ShellNumber(arr.GetInt32()),
                JsonValueKind.String => new ShellText(arr.GetString() ?? string.Empty),
                JsonValueKind.True => new ShellBool(true),
                JsonValueKind.False => new ShellBool(false),
                JsonValueKind.Null => new ShellText("null"),
                JsonValueKind.Object or JsonValueKind.Array => new ShellJson(arr),
                _ => new ShellText(arr.ToString()),
            };

            shell.SetVariable(this.VariableName, elementValue);
            try
            {
                commandState = await this.Statement.RunAsync(shell, commandState, token);
            }
            catch (PositionalException)
            {
                throw;
            }
            catch (Exception e)
            {
                var content = shell.CurrentScriptContent;
                var fileName = shell.CurrentScriptFileName;
                if (!string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(fileName))
                {
                    var (line, column, lineText) = PositionalErrorHelper.GetLineAndColumn(content, this.Statement.Start);
                    throw new PositionalException(fileName, e, line, column, lineText);
                }

                throw;
            }

            if (commandState.BreakBlock)
            {
                commandState.BreakBlock = false; // Reset break state
                return commandState;
            }

            if (commandState.IsError)
            {
                return commandState;
            }
        }

        return commandState;
    }

    public override string ToString()
    {
        return $"for ${this.VariableName} in {this.Collection} {this.Statement}";
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}