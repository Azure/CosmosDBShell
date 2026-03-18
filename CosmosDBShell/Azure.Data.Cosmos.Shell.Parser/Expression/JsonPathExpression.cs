// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.ArgumentParser;
using Azure.Data.Cosmos.Shell.Core;

internal class JSonPathExpression : Expression
{
    private readonly Token variableToken;

    public JSonPathExpression(Token variableToken, string jsonPath)
    {
        this.variableToken = variableToken ?? throw new ArgumentNullException(nameof(variableToken));
        this.JSonPath = jsonPath ?? throw new ArgumentNullException(nameof(jsonPath));
    }

    public string JSonPath { get; }

    public Token VariableToken { get => this.variableToken; }

    public override int Start => this.variableToken.Start;

    public override int Length => this.variableToken.Length;

    public override Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // Two supported forms:
        // 1) Piped-result JSON path: ".name", ".[0]", ".users[0].name" (written as $.name, $.[0], ...)
        // 2) Variable-prefixed path: "script.name", "items[0]" (written as $script.name, $items[0])

        // Piped result form: use currentState.Result as the base.
        if (this.JSonPath.StartsWith(".", StringComparison.Ordinal))
        {
            return Task.FromResult<ShellObject>(new ShellJson(JsonOperationParser.Evaluate(interpreter, currentState, this.JSonPath)));
        }

        // Variable form: evaluate the base variable first, then apply the remainder as a JSON path.
        var path = this.JSonPath;
        int splitIdx = path.IndexOfAny(['.', '[']);
        var variableName = splitIdx < 0 ? path : path[..splitIdx];
        var remainder = splitIdx < 0 ? string.Empty : path[splitIdx..];

        var baseValue = interpreter.GetVariable(variableName);

        if (string.IsNullOrEmpty(remainder))
        {
            return Task.FromResult(baseValue);
        }

        // JsonOperationParser expects to operate on state.Result and ConvertShellObject(DataType.Json)
        // must succeed.
        var jsonBase = baseValue.ConvertShellObject(DataType.Json);
        if (jsonBase is not System.Text.Json.JsonElement je)
        {
            throw new InvalidOperationException("Cannot apply JSON path to non-JSON value. Expected JSON but got: " + (baseValue?.GetType().Name ?? "null"));
        }

        var tempState = new CommandState { Result = new ShellJson(je) };
        return Task.FromResult<ShellObject>(new ShellJson(JsonOperationParser.Evaluate(interpreter, tempState, remainder)));
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        return this.JSonPath;
    }
}
