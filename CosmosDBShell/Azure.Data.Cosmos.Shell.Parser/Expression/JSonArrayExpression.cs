// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

internal class JsonArrayExpression : Expression
{
    private readonly Token lBracketToken;
    private readonly Token rBracketToken;

    public JsonArrayExpression(Token lBracketToken, Token rBracketToken, List<Expression> expressions)
    {
        this.Expressions = expressions ?? throw new ArgumentNullException(nameof(expressions));
        this.lBracketToken = lBracketToken ?? throw new ArgumentNullException(nameof(lBracketToken));
        this.rBracketToken = rBracketToken ?? throw new ArgumentNullException(nameof(rBracketToken));
    }

    public List<Expression> Expressions { get; }

    public Token LBracketToken { get => this.lBracketToken; }

    public Token RBracketToken { get => this.rBracketToken; }

    public override int Start => this.lBracketToken.Start;

    public override int Length => this.rBracketToken.Start - this.lBracketToken.Start + 1;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // Build a plain .NET list and serialize directly to a JsonElement (no parse roundtrip)
        var items = new List<object?>(this.Expressions.Count);

        foreach (var expr in this.Expressions)
        {
            var value = await expr.EvaluateAsync(interpreter, currentState, cancellationToken);
            switch (value.DataType)
            {
                case DataType.Json:
                    var jsonObj = value.ConvertShellObject(DataType.Json);
                    if (jsonObj is JsonElement jsonElement)
                    {
                        items.Add(jsonElement);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {value.GetType().Name} to JsonElement");
                    }

                    break;

                case DataType.Number:
                    var numberObj = value.ConvertShellObject(DataType.Number);
                    if (numberObj is int intValue)
                    {
                        items.Add(intValue);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {value.GetType().Name} to integer");
                    }

                    break;

                case DataType.Decimal:
                    var decimalObj = value.ConvertShellObject(DataType.Decimal);
                    if (decimalObj is double doubleValue)
                    {
                        items.Add(doubleValue);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {value.GetType().Name} to decimal");
                    }

                    break;

                case DataType.Boolean:
                    var boolObj = value.ConvertShellObject(DataType.Boolean);
                    if (boolObj is bool boolValue)
                    {
                        items.Add(boolValue);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {value.GetType().Name} to boolean");
                    }

                    break;

                case DataType.Text:
                    var textObj = value.ConvertShellObject(DataType.Text);
                    if (textObj is string textValue)
                    {
                        items.Add(textValue);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {value.GetType().Name} to string");
                    }

                    break;

                default:
                    // For unknown types, convert to string
                    var stringValue = value.ToString();
                    if (stringValue != null)
                    {
                        items.Add(stringValue);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {value.GetType().Name} to string representation");
                    }

                    break;
            }
        }

        var element = JsonSerializer.SerializeToElement(items);
        return new ShellJson(element);
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        var elements = string.Join(", ", this.Expressions.Select(e => e.ToString()));
        return $"[{elements}]";
    }
}
