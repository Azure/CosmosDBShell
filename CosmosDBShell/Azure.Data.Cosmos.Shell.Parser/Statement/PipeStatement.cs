// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;
using System.Data;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Spectre.Console;

/// <summary>
/// Represents a pipe statement that chains multiple commands, passing output between them.
/// </summary>
/// <remarks>
/// The pipe statement allows command composition by passing the result of one command
/// as input to the next command in the pipeline. Each command processes the output
/// of the previous command, enabling powerful data transformations.
///
/// Syntax:
/// command1 | command2 | command3
///
/// Example:
/// list | grep "test" | count
///
/// The pipe operator stops execution if any command in the pipeline returns an error.
/// The final result is printed once after all commands have executed successfully.
/// </remarks>
internal class PipeStatement : Statement
{
    public PipeStatement(List<Statement> statements)
    {
        this.Statements = statements ?? throw new ArgumentNullException(nameof(statements));
        if (statements.Count == 0)
        {
            throw new ArgumentException("Pipe statement must contain at least one statement.", nameof(statements));
        }
    }

    /// <summary>
    /// Gets the list of statements in the pipeline.
    /// </summary>
    public List<Statement> Statements { get; }

    /// <summary>
    /// Gets the starting position of the pipe statement in the source text.
    /// </summary>
    public override int Start => this.Statements.Count > 0 ? this.Statements[0].Start : 0;

    /// <summary>
    /// Gets the total length of the pipe statement in the source text.
    /// </summary>
    public override int Length
    {
        get
        {
            if (this.Statements.Count == 0)
            {
                return 0;
            }

            var lastStatement = this.Statements[^1];
            return (lastStatement.Start + lastStatement.Length) - this.Start;
        }
    }

    /// <summary>
    /// Executes the pipeline by running each statement sequentially.
    /// </summary>
    /// <remarks>
    /// Each statement in the pipeline receives the command state from the previous
    /// statement. If any statement returns an error, the pipeline stops and returns
    /// the error state. The final result is printed after all statements execute.
    /// </remarks>
    public override async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        foreach (var statement in this.Statements)
        {
            if (commandState.IsError)
            {
                return commandState;
            }

            commandState = await statement.RunAsync(shell, commandState, token);
        }

        shell.PrintState(commandState);
        return commandState;
    }

    public override string ToString()
    {
        return string.Join(" | ", this.Statements.Select(s => s.ToString()));
    }

    /// <summary>
    /// Accepts a visitor for AST traversal.
    /// </summary>
    internal override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}