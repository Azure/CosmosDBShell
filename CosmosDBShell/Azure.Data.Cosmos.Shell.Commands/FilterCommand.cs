//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

[CosmosCommand("filter")]
[CosmosExample("query \"SELECT * FROM c\" | filter '.items[0]'", Description = "Extract the first query result")]
[CosmosExample("query \"SELECT * FROM c\" | filter '.items | map({id, status})'", Description = "Project selected fields from query results")]
[CosmosExample("ls | filter '.items | length'", Description = "Count listed items")]
[CosmosExample("query \"SELECT * FROM c\" | filter '.items[] | .id'", Description = "Extract ids from each item")]
internal class FilterCommand : CosmosCommand
{
    [CosmosParameter("expression")]
    public string? ExpressionText { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.ExpressionText))
        {
            throw new CommandException("filter", MessageService.GetString("command-filter-error-no_expression"));
        }

        if (commandState.Result == null)
        {
            throw new CommandException("filter", MessageService.GetString("command-filter-error-no_input"));
        }

        var evaluatedInput = commandState.Result.ConvertShellObject(DataType.Json);
        if (evaluatedInput is not System.Text.Json.JsonElement)
        {
            throw new CommandException("filter", MessageService.GetString("command-filter-error-invalid_input"));
        }

        var lexer = new Lexer(this.ExpressionText);
        var parser = new ExpressionParser(lexer);
        var expression = parser.ParseFilterExpression();

        if (lexer.Errors.HasErrors)
        {
            throw new CommandException("filter", lexer.Errors[0].Message);
        }

        var result = await expression.EvaluateAsync(shell, commandState, token);
        if (result is ShellSequence sequence)
        {
            commandState.Result = new ShellJson(FilterExpressionUtilities.ToJsonArray(sequence.Elements));
        }
        else if (result is ShellJson)
        {
            commandState.Result = result;
        }
        else
        {
            commandState.Result = new ShellJson(FilterExpressionUtilities.ToJsonElement(result));
        }

        commandState.IsPrinted = false;
        return commandState;
    }
}