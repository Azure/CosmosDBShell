// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

internal class BinaryOperatorExpression : Expression
{
    public BinaryOperatorExpression(Expression left, Token op, Expression right)
    {
        this.Left = left ?? throw new ArgumentNullException(nameof(left));
        this.Right = right ?? throw new ArgumentNullException(nameof(right));
        this.OperatorToken = op ?? throw new ArgumentNullException(nameof(op));
    }

    public Expression Left { get; }

    public Token OperatorToken { get; }

    public Expression Right { get; }

    public TokenType Operator { get => this.OperatorToken.Type; }

    public override int Start => this.Left.Start;

    public override int Length => this.Right.Start + this.Right.Length - this.Left.Start;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // Handle short-circuit evaluation for logical operators
        if (this.Operator == TokenType.And || this.Operator == TokenType.Or)
        {
            var lr = await this.Left.EvaluateAsync(interpreter, currentState, cancellationToken);
            var leftBoolObj = lr.ConvertShellObject(DataType.Boolean);
            if (leftBoolObj == null)
            {
                throw new InvalidOperationException("Left operand evaluation returned null for boolean operation");
            }

            var leftBool = (bool)leftBoolObj;

            // Short-circuit evaluation
            if (this.Operator == TokenType.And && !leftBool)
            {
                return new ShellBool(false);
            }

            if (this.Operator == TokenType.Or && leftBool)
            {
                return new ShellBool(true);
            }

            // ConvertShellObject right side only if needed
            var rr = await this.Right.EvaluateAsync(interpreter, currentState, cancellationToken);
            var rightBoolObj = rr.ConvertShellObject(DataType.Boolean);
            if (rightBoolObj == null)
            {
                throw new InvalidOperationException("Right operand evaluation returned null for boolean operation");
            }

            var rightBool = (bool)rightBoolObj;

            return new ShellBool(this.Operator == TokenType.And ? leftBool && rightBool : leftBool || rightBool);
        }

        // For all other operators, evaluate both sides first
        var leftResult = await this.Left.EvaluateAsync(interpreter, currentState, cancellationToken);
        var rightResult = await this.Right.EvaluateAsync(interpreter, currentState, cancellationToken);

        // Handle arithmetic operators
        // Handle arithmetic operators
        switch (this.Operator)
        {
            case TokenType.Plus:
                // JSON array concatenation: [] + [5] => [..]
                if (leftResult.DataType == DataType.Json &&
                    rightResult.DataType == DataType.Json)
                {
                    var leftJsonObj = leftResult.ConvertShellObject(DataType.Json);
                    var rightJsonObj = rightResult.ConvertShellObject(DataType.Json);
                    if (leftJsonObj == null || rightJsonObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for JSON array concatenation");
                    }

                    var lje = (JsonElement)leftJsonObj;
                    var rje = (JsonElement)rightJsonObj;

                    if (lje.ValueKind == JsonValueKind.Array && rje.ValueKind == JsonValueKind.Array)
                    {
                        using var ms = new MemoryStream();
                        using (var writer = new Utf8JsonWriter(ms))
                        {
                            writer.WriteStartArray();
                            foreach (var el in lje.EnumerateArray())
                            {
                                el.WriteTo(writer);
                            }

                            foreach (var el in rje.EnumerateArray())
                            {
                                el.WriteTo(writer);
                            }

                            writer.WriteEndArray();
                            writer.Flush();
                        }

                        ms.Position = 0;
                        using var doc = JsonDocument.Parse(ms);
                        return new ShellJson(doc.RootElement.Clone());
                    }
                }

                // For strings, concatenate
                if (leftResult.DataType == DataType.Text || rightResult.DataType == DataType.Text)
                {
                    var leftStrObj = leftResult.ConvertShellObject(DataType.Text);
                    var rightStrObj = rightResult.ConvertShellObject(DataType.Text);
                    var leftStr = leftStrObj?.ToString() ?? string.Empty;
                    var rightStr = rightStrObj?.ToString() ?? string.Empty;
                    return new ShellText(leftStr + rightStr);
                }

                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal addition");
                    }

                    return new ShellDecimal((double)leftDecObj + (double)rightDecObj);
                }

                // For numbers, add
                var leftNumObj1 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj1 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj1 == null || rightNumObj1 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for numeric addition");
                }

                return new ShellNumber((int)leftNumObj1 + (int)rightNumObj1);

            case TokenType.Minus:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal subtraction");
                    }

                    return new ShellDecimal((double)leftDecObj - (double)rightDecObj);
                }

                var leftNumObj2 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj2 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj2 == null || rightNumObj2 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for numeric subtraction");
                }

                return new ShellNumber((int)leftNumObj2 - (int)rightNumObj2);

            case TokenType.Multiply:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal multiplication");
                    }

                    return new ShellDecimal((double)leftDecObj * (double)rightDecObj);
                }

                var leftNumObj3 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj3 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj3 == null || rightNumObj3 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for numeric multiplication");
                }

                return new ShellNumber((int)leftNumObj3 * (int)rightNumObj3);

            case TokenType.Divide:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal division");
                    }

                    var rightDec = (double)rightDecObj;
                    if (rightDec == 0.0)
                    {
                        throw new DivideByZeroException("Division by zero");
                    }

                    return new ShellDecimal((double)leftDecObj / rightDec);
                }

                var leftNumObj4 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj4 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj4 == null || rightNumObj4 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for numeric division");
                }

                var rightNum4 = (int)rightNumObj4;
                if (rightNum4 == 0)
                {
                    throw new DivideByZeroException("Division by zero");
                }

                return new ShellNumber((int)leftNumObj4 / rightNum4);

            case TokenType.Mod:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal modulo operation");
                    }

                    var rightDec = (double)rightDecObj;
                    if (rightDec == 0.0)
                    {
                        throw new DivideByZeroException("Modulo by zero");
                    }

                    return new ShellDecimal((double)leftDecObj % rightDec);
                }

                var leftNumObj5 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj5 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj5 == null || rightNumObj5 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for modulo operation");
                }

                var rightNum5 = (int)rightNumObj5;
                if (rightNum5 == 0)
                {
                    throw new DivideByZeroException("Modulo by zero");
                }

                return new ShellNumber((int)leftNumObj5 % rightNum5);

            case TokenType.Pow:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal power operation");
                    }

                    return new ShellDecimal(Math.Pow((double)leftDecObj, (double)rightDecObj));
                }

                var leftNumObj6 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj6 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj6 == null || rightNumObj6 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for power operation");
                }

                var rightNum6 = (int)rightNumObj6;
                if (rightNum6 < 0)
                {
                    throw new NotSupportedException("Negative exponents are not supported for integer power operation.");
                }

                return new ShellNumber((int)Math.Pow((int)leftNumObj6, rightNum6));

            // Comparison operators
            case TokenType.Equal:
                // Try to compare as same type
                if (leftResult.DataType == rightResult.DataType)
                {
                    switch (leftResult.DataType)
                    {
                        case DataType.Number:
                            var leftNumObjEq = leftResult.ConvertShellObject(DataType.Number);
                            var rightNumObjEq = rightResult.ConvertShellObject(DataType.Number);
                            if (leftNumObjEq == null || rightNumObjEq == null)
                            {
                                throw new InvalidOperationException("Operand evaluation returned null for numeric equality comparison");
                            }

                            return new ShellBool((int)leftNumObjEq == (int)rightNumObjEq);
                        case DataType.Decimal:
                            var leftDecObjEq = leftResult.ConvertShellObject(DataType.Decimal);
                            var rightDecObjEq = rightResult.ConvertShellObject(DataType.Decimal);
                            if (leftDecObjEq == null || rightDecObjEq == null)
                            {
                                throw new InvalidOperationException("Operand evaluation returned null for decimal equality comparison");
                            }

                            return new ShellBool((double)leftDecObjEq == (double)rightDecObjEq);
                        case DataType.Boolean:
                            var leftBoolObjEq = leftResult.ConvertShellObject(DataType.Boolean);
                            var rightBoolObjEq = rightResult.ConvertShellObject(DataType.Boolean);
                            if (leftBoolObjEq == null || rightBoolObjEq == null)
                            {
                                throw new InvalidOperationException("Operand evaluation returned null for boolean equality comparison");
                            }

                            return new ShellBool((bool)leftBoolObjEq == (bool)rightBoolObjEq);
                        default:
                            var leftStrObjEq = leftResult.ConvertShellObject(DataType.Text);
                            var rightStrObjEq = rightResult.ConvertShellObject(DataType.Text);
                            var leftStrEq = leftStrObjEq?.ToString() ?? string.Empty;
                            var rightStrEq = rightStrObjEq?.ToString() ?? string.Empty;
                            return new ShellBool(leftStrEq == rightStrEq);
                    }
                }
                else
                {
                    // Convert both to text for comparison
                    var leftStrObjDiff = leftResult.ConvertShellObject(DataType.Text);
                    var rightStrObjDiff = rightResult.ConvertShellObject(DataType.Text);
                    var leftStrDiff = leftStrObjDiff?.ToString() ?? string.Empty;
                    var rightStrDiff = rightStrObjDiff?.ToString() ?? string.Empty;
                    return new ShellBool(leftStrDiff == rightStrDiff);
                }

            case TokenType.NotEqual:
                // Evaluate equality and negate
                var equalToken = new Token(TokenType.Equal, "==", this.OperatorToken.Start, this.OperatorToken.Length);
                var equalResult = await new BinaryOperatorExpression(this.Left, equalToken, this.Right)
                    .EvaluateAsync(interpreter, currentState, cancellationToken);
                var isEqualObj = equalResult.ConvertShellObject(DataType.Boolean);
                if (isEqualObj == null)
                {
                    throw new InvalidOperationException("Equality evaluation returned null for not-equal comparison");
                }

                return new ShellBool(!(bool)isEqualObj);

            case TokenType.LessThan:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal less-than comparison");
                    }

                    return new ShellBool((double)leftDecObj < (double)rightDecObj);
                }

                var leftNumObj7 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj7 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj7 == null || rightNumObj7 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for less-than comparison");
                }

                return new ShellBool((int)leftNumObj7 < (int)rightNumObj7);

            case TokenType.GreaterThan:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal greater-than comparison");
                    }

                    return new ShellBool((double)leftDecObj > (double)rightDecObj);
                }

                var leftNumObj8 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj8 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj8 == null || rightNumObj8 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for greater-than comparison");
                }

                return new ShellBool((int)leftNumObj8 > (int)rightNumObj8);

            case TokenType.LessThanOrEqual:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal less-than-or-equal comparison");
                    }

                    return new ShellBool((double)leftDecObj <= (double)rightDecObj);
                }

                var leftNumObj9 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj9 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj9 == null || rightNumObj9 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for less-than-or-equal comparison");
                }

                return new ShellBool((int)leftNumObj9 <= (int)rightNumObj9);

            case TokenType.GreaterThanOrEqual:
                // Check if either operand is decimal
                if (leftResult.DataType == DataType.Decimal || rightResult.DataType == DataType.Decimal)
                {
                    var leftDecObj = leftResult.ConvertShellObject(DataType.Decimal);
                    var rightDecObj = rightResult.ConvertShellObject(DataType.Decimal);
                    if (leftDecObj == null || rightDecObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for decimal greater-than-or-equal comparison");
                    }

                    return new ShellBool((double)leftDecObj >= (double)rightDecObj);
                }

                var leftNumObj10 = leftResult.ConvertShellObject(DataType.Number);
                var rightNumObj10 = rightResult.ConvertShellObject(DataType.Number);
                if (leftNumObj10 == null || rightNumObj10 == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for greater-than-or-equal comparison");
                }

                return new ShellBool((int)leftNumObj10 >= (int)rightNumObj10);

            // Boolean operators
            case TokenType.Xor:
                var leftBoolObjXor = leftResult.ConvertShellObject(DataType.Boolean);
                var rightBoolObjXor = rightResult.ConvertShellObject(DataType.Boolean);
                if (leftBoolObjXor == null || rightBoolObjXor == null)
                {
                    throw new InvalidOperationException("Operand evaluation returned null for XOR operation");
                }

                return new ShellBool((bool)leftBoolObjXor ^ (bool)rightBoolObjXor);

            default:
                throw new NotSupportedException($"Binary operator {this.Operator} is not supported");
        }
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        return $"({this.Left} {this.OperatorToken.Value} {this.Right})";
    }
}