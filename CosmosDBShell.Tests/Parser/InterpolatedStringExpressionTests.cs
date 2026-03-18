// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

namespace CosmosShell.Tests.Parser
{
    public class InterpolatedStringExpressionTests
    {
        private Expression ParseExpression(string input)
        {
            var lexer = new Lexer(input);
            var parser = new ExpressionParser(lexer);
            return parser.ParseExpression();
        }

        private (Expression? Expr, int ErrorCount, Exception? Exception) TryParse(string input)
        {
            var lexer = new Lexer(input);
            var parser = new ExpressionParser(lexer);
            Expression? expr = null;
            Exception? ex = Record.Exception(() => expr = parser.ParseExpression());
            return (expr, lexer.Errors.Count, ex);
        }

        private async Task<ShellObject> EvaluateExpressionAsync(string input)
        {
            var expression = ParseExpression(input);
            var interpreter = new ShellInterpreter();
            return await expression.EvaluateAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        [Fact]
        public void ParseInterpolatedString_NoInterpolation_ReturnsInterpolatedStringExpression()
        {
            var expr = ParseExpression("$\"Hello World\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Single(interpExpr.Expressions);
            var constExpr = Assert.IsType<ConstantExpression>(interpExpr.Expressions[0]);
            var text = Assert.IsType<ShellText>(constExpr.Value);
            Assert.Equal("Hello World", text.Text);
        }

        [Fact]
        public void ParseInterpolatedString_SingleVariable_CreatesVariableExpression()
        {
            var expr = ParseExpression("$\"Hello $name!\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(3, interpExpr.Expressions.Count);

            var text1 = Assert.IsType<ConstantExpression>(interpExpr.Expressions[0]);
            Assert.Equal("Hello ", ((ShellText)text1.Value).Text);

            var var1 = Assert.IsType<VariableExpression>(interpExpr.Expressions[1]);
            Assert.Equal("name", var1.Name);

            var text2 = Assert.IsType<ConstantExpression>(interpExpr.Expressions[2]);
            Assert.Equal("!", ((ShellText)text2.Value).Text);
        }

        [Fact]
        public void ParseInterpolatedString_MultipleVariables_CreatesCorrectExpressions()
        {
            var expr = ParseExpression("$\"$firstName $lastName\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(3, interpExpr.Expressions.Count);

            Assert.Equal("firstName", Assert.IsType<VariableExpression>(interpExpr.Expressions[0]).Name);
            Assert.Equal(" ", ((ShellText)((ConstantExpression)interpExpr.Expressions[1]).Value).Text);
            Assert.Equal("lastName", Assert.IsType<VariableExpression>(interpExpr.Expressions[2]).Name);
        }

        [Fact]
        public void ParseInterpolatedString_ExpressionInterpolation_ParsesExpression()
        {
            var expr = ParseExpression("$\"Result: $(2 + 3)\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(2, interpExpr.Expressions.Count);

            var text = Assert.IsType<ConstantExpression>(interpExpr.Expressions[0]);
            Assert.Equal("Result: ", ((ShellText)text.Value).Text);

            var binExpr = Assert.IsType<BinaryOperatorExpression>(interpExpr.Expressions[1]);
            Assert.Equal(TokenType.Plus, binExpr.Operator);
        }

        [Fact]
        public void ParseInterpolatedString_NestedParenthesesInExpression_ParsesCorrectly()
        {
            var expr = ParseExpression("$\"Value: $((2 + 3) * (4 + 5))\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(2, interpExpr.Expressions.Count);
            var binExpr = Assert.IsType<BinaryOperatorExpression>(interpExpr.Expressions[1]);
            Assert.Equal(TokenType.Multiply, binExpr.Operator);
        }

        [Fact]
        public void ParseInterpolatedString_ComplexMixed_ParsesAllParts()
        {
            var expr = ParseExpression("$\"User $name has $(count + 1) items\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(5, interpExpr.Expressions.Count);

            Assert.Equal("User ", ((ShellText)((ConstantExpression)interpExpr.Expressions[0]).Value).Text);
            Assert.Equal("name", ((VariableExpression)interpExpr.Expressions[1]).Name);
            Assert.Equal(" has ", ((ShellText)((ConstantExpression)interpExpr.Expressions[2]).Value).Text);
            Assert.IsType<BinaryOperatorExpression>(interpExpr.Expressions[3]);
            Assert.Equal(" items", ((ShellText)((ConstantExpression)interpExpr.Expressions[4]).Value).Text);
        }

        [Fact]
        public void ParseInterpolatedString_DollarAtEnd_TreatsAsLiteral()
        {
            var expr = ParseExpression("$\"Price: $\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(2, interpExpr.Expressions.Count);
            Assert.Equal("Price: ", ((ShellText)((ConstantExpression)interpExpr.Expressions[0]).Value).Text);
            Assert.Equal("$", ((ShellText)((ConstantExpression)interpExpr.Expressions[1]).Value).Text);
        }

        [Fact]
        public void ParseInterpolatedString_DollarFollowedByInvalidChar_TreatsAsLiteral()
        {
            var expr = ParseExpression("$\"Amount: $100\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            // $100 is now parsed as a variable named "100" since we support positional parameters starting with digits
            Assert.Equal(2, interpExpr.Expressions.Count);
            Assert.Equal("Amount: ", ((ShellText)((ConstantExpression)interpExpr.Expressions[0]).Value).Text);
            // $100 is recognized as a positional parameter variable with name "100"
            var varExpr = Assert.IsType<VariableExpression>(interpExpr.Expressions[1]);
            Assert.Equal("100", varExpr.Name);
        }

        [Fact]
        public void ParseInterpolatedString_EmptyExpression_IgnoresIt()
        {
            var expr = ParseExpression("$\"Value: $()\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Single(interpExpr.Expressions);
            Assert.Equal("Value: ", ((ShellText)((ConstantExpression)interpExpr.Expressions[0]).Value).Text);
        }

        [Fact]
        public void ParseInterpolatedString_UnmatchedParentheses_ReportsError()
        {
            var result = TryParse("$\"Value: $(2 + 3\"");
            Assert.True(result.Exception != null || result.ErrorCount > 0,
                "Expected parse error (exception or lexer errors) for unmatched parentheses in interpolated string.");
        }

        [Fact]
        public async Task EvaluateInterpolatedString_WithVariables_SubstitutesCorrectly()
        {
            var interpreter = new ShellInterpreter();
            interpreter.SetVariable("name", new ShellText("John"));
            interpreter.SetVariable("age", new ShellNumber(30));

            var expr = ParseExpression("$\"Hello $name, you are $age years old\"");
            var result = await expr.EvaluateAsync(interpreter, new CommandState(), CancellationToken.None);

            var txt = Assert.IsType<ShellText>(result);
            Assert.Equal("Hello John, you are 30 years old", txt.Text);
        }

        [Fact]
        public async Task EvaluateInterpolatedString_WithExpressions_EvaluatesCorrectly()
        {
            var interpreter = new ShellInterpreter();
            interpreter.SetVariable("x", new ShellNumber(10));
            interpreter.SetVariable("y", new ShellNumber(5));

            var expr = ParseExpression("$\"The sum of $x and $y is $($x + $y)\"");
            var result = await expr.EvaluateAsync(interpreter, new CommandState(), CancellationToken.None);

            var txt = Assert.IsType<ShellText>(result);
            Assert.Equal("The sum of 10 and 5 is 15", txt.Text);
        }

        [Fact]
        public async Task EvaluateInterpolatedString_ComplexExpression_EvaluatesCorrectly()
        {
            var interpreter = new ShellInterpreter();
            interpreter.SetVariable("items", new ShellNumber(5));
            interpreter.SetVariable("price", new ShellDecimal(9.99));

            var expr = ParseExpression("$\"Total for $items items: \\$$($items * $price)\"");
            var result = await expr.EvaluateAsync(interpreter, new CommandState(), CancellationToken.None);

            var txt = Assert.IsType<ShellText>(result);
            Assert.Equal("Total for 5 items: $49.95", txt.Text);
        }

        [Fact]
        public void ParseInterpolatedString_VariableWithUnderscoreAndDash_ParsesCorrectly()
        {
            var expr = ParseExpression("$\"Value: $my_var-name\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(2, interpExpr.Expressions.Count);
            Assert.Equal("Value: ", ((ShellText)((ConstantExpression)interpExpr.Expressions[0]).Value).Text);
            Assert.Equal("my_var-name", ((VariableExpression)interpExpr.Expressions[1]).Name);
        }

        [Fact]
        public void ParseInterpolatedString_EscapedCharacters_HandledByLexer()
        {
            var expr = ParseExpression("$\"Line 1\\nLine 2\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Single(interpExpr.Expressions);
            var text = ((ShellText)((ConstantExpression)interpExpr.Expressions[0]).Value).Text;
            Assert.Contains("\n", text);
        }

        [Fact]
        public void ParseInterpolatedString_PositionalParameters_ParsesCorrectly()
        {
            var expr = ParseExpression("$\"Running script $0 against account $1\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(4, interpExpr.Expressions.Count);

            Assert.Equal("Running script ", ((ShellText)((ConstantExpression)interpExpr.Expressions[0]).Value).Text);
            Assert.Equal("0", ((VariableExpression)interpExpr.Expressions[1]).Name);
            Assert.Equal(" against account ", ((ShellText)((ConstantExpression)interpExpr.Expressions[2]).Value).Text);
            Assert.Equal("1", ((VariableExpression)interpExpr.Expressions[3]).Name);
        }

        [Fact]
        public async Task EvaluateInterpolatedString_WithPositionalParameters_SubstitutesCorrectly()
        {
            var interpreter = new ShellInterpreter();
            interpreter.SetVariable("0", new ShellText("script.csh"));
            interpreter.SetVariable("1", new ShellText("myaccount"));
            interpreter.SetVariable("2", new ShellText("somevalue"));

            var expr = ParseExpression("$\"Running script $0 against account $1 with param $2\"");
            var result = await expr.EvaluateAsync(interpreter, new CommandState(), CancellationToken.None);

            var txt = Assert.IsType<ShellText>(result);
            Assert.Equal("Running script script.csh against account myaccount with param somevalue", txt.Text);
        }

        [Fact]
        public void ParseInterpolatedString_MixedPositionalAndNamedVariables_ParsesCorrectly()
        {
            var expr = ParseExpression("$\"Script $0 running with $name at $1\"");
            var interpExpr = Assert.IsType<InterpolatedStringExpression>(expr);
            Assert.Equal(6, interpExpr.Expressions.Count);

            Assert.Equal("Script ", ((ShellText)((ConstantExpression)interpExpr.Expressions[0]).Value).Text);
            Assert.Equal("0", ((VariableExpression)interpExpr.Expressions[1]).Name);
            Assert.Equal(" running with ", ((ShellText)((ConstantExpression)interpExpr.Expressions[2]).Value).Text);
            Assert.Equal("name", ((VariableExpression)interpExpr.Expressions[3]).Name);
            Assert.Equal(" at ", ((ShellText)((ConstantExpression)interpExpr.Expressions[4]).Value).Text);
            Assert.Equal("1", ((VariableExpression)interpExpr.Expressions[5]).Name);
        }
    }
}