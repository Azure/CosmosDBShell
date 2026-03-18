// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

// JsonExpression.cs
internal class JsonExpression : Expression
{
    private readonly Token lBraceToken;
    private readonly Token rBraceToken;

    public JsonExpression(Token lBraceToken, Token rBraceToken, Dictionary<ShellObject, Expression> properties)
    {
        this.Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        this.lBraceToken = lBraceToken ?? throw new ArgumentNullException(nameof(lBraceToken));
        this.rBraceToken = rBraceToken ?? throw new ArgumentNullException(nameof(rBraceToken));
    }

    public Dictionary<ShellObject, Expression> Properties { get; }

    public override int Start => this.lBraceToken.Start;

    public override int Length => this.rBraceToken.Start - this.lBraceToken.Start + 1;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // Build a dictionary to serialize
        var dict = new Dictionary<string, object?>();

        foreach (var kvp in this.Properties)
        {
            // Convert the key to get the property name
            var keyObj = kvp.Key.ConvertShellObject(DataType.Text);
            if (keyObj is not string key)
            {
                throw new InvalidOperationException($"Failed to convert property key {kvp.Key.GetType().Name} to string");
            }

            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Property key cannot be null or empty");
            }

            // Evaluate the value expression
            var valueExpr = await kvp.Value.EvaluateAsync(interpreter, currentState, cancellationToken);

            if (valueExpr is ShellIdentifier id)
            {
                if (id.Value == "null")
                {
                    // If the identifier is "null", we treat it as a null value
                    dict[key] = null;
                    continue;
                }
            }

            // Convert to appropriate type for JSON serialization
            object? value;
            switch (valueExpr.DataType)
            {
                case DataType.Json:
                    var jsonObj = valueExpr.ConvertShellObject(DataType.Json);
                    if (jsonObj is JsonElement jsonElement)
                    {
                        value = jsonElement;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {valueExpr.GetType().Name} to JsonElement");
                    }

                    break;

                case DataType.Number:
                    var numberObj = valueExpr.ConvertShellObject(DataType.Number);
                    if (numberObj is int intValue)
                    {
                        value = intValue;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {valueExpr.GetType().Name} to integer");
                    }

                    break;

                case DataType.Decimal:
                    var decimalObj = valueExpr.ConvertShellObject(DataType.Decimal);
                    if (decimalObj is double doubleValue)
                    {
                        value = doubleValue;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {valueExpr.GetType().Name} to decimal");
                    }

                    break;

                case DataType.Boolean:
                    var boolObj = valueExpr.ConvertShellObject(DataType.Boolean);
                    if (boolObj is bool boolValue)
                    {
                        value = boolValue;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {valueExpr.GetType().Name} to boolean");
                    }

                    break;

                case DataType.Text:
                    var textObj = valueExpr.ConvertShellObject(DataType.Text);
                    if (textObj is string textValue)
                    {
                        value = textValue;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {valueExpr.GetType().Name} to string");
                    }

                    break;

                default:
                    var stringValue = valueExpr.ToString();
                    if (stringValue != null)
                    {
                        value = stringValue;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to convert {valueExpr.GetType().Name} to string representation");
                    }

                    break;
            }

            dict[key] = value;
        }

        // Serialize the dictionary to a JsonElement
        var element = JsonSerializer.SerializeToElement(dict);
        return new ShellJson(element);
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        var props = string.Join(", ", this.Properties.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
        return $"{{ {props} }}";
    }
}