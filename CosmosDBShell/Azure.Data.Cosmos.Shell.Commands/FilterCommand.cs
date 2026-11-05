//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

[CosmosCommand("filter")]
[CosmosExample("query \"SELECT * FROM c\" | filter '.items[0]'", DescriptionKey = "command-filter-example-1")]
[CosmosExample("query \"SELECT * FROM c\" | filter '.items | map({id, status})'", DescriptionKey = "command-filter-example-2")]
[CosmosExample("ls | filter '.items | length'", DescriptionKey = "command-filter-example-3")]
[CosmosExample("query \"SELECT * FROM c\" | filter '.items[] | .id'", DescriptionKey = "command-filter-example-4")]
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

        if (!parser.IsAtEnd)
        {
            var trailing = parser.Current?.Value ?? string.Empty;
            throw new CommandException(
                "filter",
                MessageService.GetString(
                    "command-filter-error-trailing_tokens",
                    new Dictionary<string, object> { { "token", trailing } }));
        }

        ShellObject result;
        try
        {
            result = await expression.EvaluateAsync(shell, commandState, token);
        }
        catch (CommandException)
        {
            throw;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CommandException(
                "filter",
                MessageService.GetString(
                    "command-filter-error-evaluation",
                    new Dictionary<string, object> { { "message", ex.Message } }));
        }

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

        return commandState;
    }
}