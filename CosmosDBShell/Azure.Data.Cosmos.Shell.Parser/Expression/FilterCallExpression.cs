// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Linq;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

internal class FilterCallExpression : Expression
{
    public FilterCallExpression(Token nameToken, IReadOnlyList<Expression> arguments, int? length = null)
    {
        this.NameToken = nameToken ?? throw new ArgumentNullException(nameof(nameToken));
        this.Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        this.ExpressionLength = length ?? nameToken.Length;
    }

    public Token NameToken { get; }

    public string Name => this.NameToken.Value;

    public IReadOnlyList<Expression> Arguments { get; }

    public int ExpressionLength { get; }

    public override int Start => this.NameToken.Start;

    public override int Length => this.ExpressionLength;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        var current = currentState.Result?.ConvertShellObject(DataType.Json);
        if (current is not JsonElement currentJson)
        {
            throw new InvalidOperationException($"filter builtin '{this.Name}' requires a JSON pipeline value");
        }

        return this.Name switch
        {
            "length" => EvaluateLength(currentJson),
            "keys" => EvaluateKeys(currentJson),
            "type" => EvaluateType(currentJson),
            "contains" => await this.EvaluateContainsAsync(interpreter, currentState, currentJson, cancellationToken),
            "map" => await this.EvaluateMapAsync(interpreter, currentJson, cancellationToken),
            "select" => await this.EvaluateSelectAsync(interpreter, currentJson, cancellationToken),
            "sort_by" => await this.EvaluateSortByAsync(interpreter, currentJson, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported filter builtin '{this.Name}'"),
        };
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    private static ShellObject EvaluateLength(JsonElement currentJson)
    {
        return currentJson.ValueKind switch
        {
            JsonValueKind.Array => new ShellNumber(currentJson.GetArrayLength()),
            JsonValueKind.Object => new ShellNumber(currentJson.EnumerateObject().Count()),
            JsonValueKind.String => new ShellNumber((currentJson.GetString() ?? string.Empty).Length),
            JsonValueKind.Null => new ShellNumber(0),
            _ => throw new InvalidOperationException("length supports arrays, objects, strings, and null"),
        };
    }

    private static ShellObject EvaluateKeys(JsonElement currentJson)
    {
        if (currentJson.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("keys requires an object input");
        }

        var keys = currentJson.EnumerateObject().Select(static p => p.Name).OrderBy(static p => p, StringComparer.Ordinal).ToArray();
        return new ShellJson(JsonSerializer.SerializeToElement(keys));
    }

    private static ShellObject EvaluateType(JsonElement currentJson)
    {
        var value = currentJson.ValueKind switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number => "number",
            JsonValueKind.String => "string",
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            _ => "null",
        };

        return new ShellText(value);
    }

    private async Task<ShellObject> EvaluateContainsAsync(ShellInterpreter interpreter, CommandState currentState, JsonElement currentJson, CancellationToken cancellationToken)
    {
        this.RequireArgumentCount(1);
        var target = await this.Arguments[0].EvaluateAsync(interpreter, currentState, cancellationToken);
        return new ShellBool(FilterExpressionUtilities.Contains(currentJson, FilterExpressionUtilities.ToJsonElement(target)));
    }

    private async Task<ShellObject> EvaluateMapAsync(ShellInterpreter interpreter, JsonElement currentJson, CancellationToken cancellationToken)
    {
        this.RequireArgumentCount(1);
        if (currentJson.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("map requires an array input");
        }

        var results = new List<JsonElement>();
        foreach (var item in currentJson.EnumerateArray())
        {
            var nestedState = new CommandState { Result = new ShellJson(item.Clone()) };
            var evaluated = await this.Arguments[0].EvaluateAsync(interpreter, nestedState, cancellationToken);
            if (evaluated is ShellSequence sequence)
            {
                results.AddRange(sequence.Elements.Select(static e => e.Clone()));
            }
            else
            {
                results.Add(FilterExpressionUtilities.ToJsonElement(evaluated));
            }
        }

        return new ShellJson(FilterExpressionUtilities.ToJsonArray(results));
    }

    private async Task<ShellObject> EvaluateSelectAsync(ShellInterpreter interpreter, JsonElement currentJson, CancellationToken cancellationToken)
    {
        this.RequireArgumentCount(1);
        if (currentJson.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("select requires an array input");
        }

        var results = new List<JsonElement>();
        foreach (var item in currentJson.EnumerateArray())
        {
            var nestedState = new CommandState { Result = new ShellJson(item.Clone()) };
            var evaluated = await this.Arguments[0].EvaluateAsync(interpreter, nestedState, cancellationToken);
            if (evaluated is ShellBool shellBool && shellBool.Value)
            {
                results.Add(item.Clone());
            }
            else if (evaluated is ShellJson json && json.Value.ValueKind == JsonValueKind.True)
            {
                results.Add(item.Clone());
            }
        }

        return new ShellJson(FilterExpressionUtilities.ToJsonArray(results));
    }

    private async Task<ShellObject> EvaluateSortByAsync(ShellInterpreter interpreter, JsonElement currentJson, CancellationToken cancellationToken)
    {
        this.RequireArgumentCount(1);
        if (currentJson.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("sort_by requires an array input");
        }

        var items = new List<(JsonElement Item, JsonElement Key)>();
        foreach (var item in currentJson.EnumerateArray())
        {
            var nestedState = new CommandState { Result = new ShellJson(item.Clone()) };
            var evaluated = await this.Arguments[0].EvaluateAsync(interpreter, nestedState, cancellationToken);
            items.Add((item.Clone(), FilterExpressionUtilities.ToJsonElement(evaluated)));
        }

        items.Sort(static (left, right) => FilterExpressionUtilities.Compare(left.Key, right.Key));
        return new ShellJson(FilterExpressionUtilities.ToJsonArray(items.Select(static item => item.Item)));
    }

    private void RequireArgumentCount(int expected)
    {
        if (this.Arguments.Count != expected)
        {
            throw new InvalidOperationException($"Builtin '{this.Name}' expects {expected} argument(s)");
        }
    }
}