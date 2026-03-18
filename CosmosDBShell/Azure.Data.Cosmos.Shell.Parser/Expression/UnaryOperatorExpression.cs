// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;

internal class UnaryOperatorExpression : Expression
{
    public UnaryOperatorExpression(Token op, Expression expression)
    {
        this.Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        this.OperatorToken = op ?? throw new ArgumentNullException(nameof(op));
    }

    public Expression Expression { get; }

    public Token OperatorToken { get; }

    public TokenType Operator { get => this.OperatorToken.Type; }

    public override int Start => this.OperatorToken.Start;

    public override int Length => this.Expression.Start + this.Expression.Length - this.OperatorToken.Start;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        // Evaluate the operand expression
        var operandResult = await this.Expression.EvaluateAsync(interpreter, currentState, cancellationToken);

        switch (this.Operator)
        {
            case TokenType.Not:
                {
                    // Logical NOT - convert operand to boolean and negate
                    var operandBoolObj = operandResult.ConvertShellObject(DataType.Boolean);
                    if (operandBoolObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for NOT operation");
                    }

                    var operandBool = (bool)operandBoolObj;
                    return new ShellBool(!operandBool);
                }

            case TokenType.Minus:
                {
                    // Check if operand is decimal type
                    if (operandResult.DataType == DataType.Decimal)
                    {
                        var operandDecObj = operandResult.ConvertShellObject(DataType.Decimal);
                        if (operandDecObj == null)
                        {
                            throw new InvalidOperationException("Operand evaluation returned null for decimal negation");
                        }

                        var operandDec = (double)operandDecObj;
                        return new ShellDecimal(-operandDec);
                    }

                    // Numeric negation - convert operand to number and negate
                    var operandNumObj = operandResult.ConvertShellObject(DataType.Number);
                    if (operandNumObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for numeric negation");
                    }

                    var operandNum = (int)operandNumObj;
                    return new ShellNumber(-operandNum);
                }

            case TokenType.Plus:
                {
                    // Check if operand is decimal type
                    if (operandResult.DataType == DataType.Decimal)
                    {
                        var operandDecObj = operandResult.ConvertShellObject(DataType.Decimal);
                        if (operandDecObj == null)
                        {
                            throw new InvalidOperationException("Operand evaluation returned null for unary plus on decimal");
                        }

                        var operandDec = (double)operandDecObj;
                        return new ShellDecimal(operandDec);
                    }

                    // Unary plus - convert operand to number (no change in value)
                    var operandNumObj = operandResult.ConvertShellObject(DataType.Number);
                    if (operandNumObj == null)
                    {
                        throw new InvalidOperationException("Operand evaluation returned null for unary plus");
                    }

                    var operandNum = (int)operandNumObj;
                    return new ShellNumber(operandNum);
                }

            default:
                throw new NotSupportedException($"Unary operator {this.Operator} is not supported");
        }
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override string ToString()
    {
        return $"{this.OperatorToken.Value}{this.Expression}";
    }
}