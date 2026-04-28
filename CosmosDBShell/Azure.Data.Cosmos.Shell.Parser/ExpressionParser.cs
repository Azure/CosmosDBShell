// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.ArgumentParser;
using Azure.Data.Cosmos.Shell.Util;

internal class ExpressionParser
{
    private readonly Lexer lexer;
    private Token? currentToken;
    private bool initialized = false;
    private Token? lastNonNullToken;
    private bool aborted = false;

    public ExpressionParser(Lexer lexer)
    {
        this.lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));
        this.currentToken = null;
    }

    public Token? Current
    {
        get
        {
            this.Initialize();
            if (this.currentToken == null)
            {
                this.AbortUnexpectedEnd();
                return null;
            }

            return this.currentToken;
        }
    }

    public bool IsAtEnd => this.currentToken == null || this.aborted;

    /// <summary>
    /// Returns the token immediately following <see cref="Current"/> without consuming it.
    /// Returns null if the input is exhausted. Comments are skipped.
    /// </summary>
    public Token? Peek()
    {
        if (this.aborted)
        {
            return null;
        }

        Token? next;
        do
        {
            next = this.lexer.NextToken();
        }
        while (next?.Type == TokenType.Comment);

        if (next != null)
        {
            this.lexer.PutBackToken(next);
        }

        return next;
    }

    /// <summary>
    /// Returns the token at <paramref name="offset"/> positions ahead of <see cref="Current"/>
    /// without consuming any tokens. <c>offset = 0</c> is the same as <see cref="Peek"/>.
    /// Comments are skipped. Returns null if the input is exhausted before that offset.
    /// </summary>
    public Token? PeekAt(int offset)
    {
        if (this.aborted || offset < 0)
        {
            return null;
        }

        var pulled = new List<Token>();
        Token? result = null;
        for (int i = 0; i <= offset; i++)
        {
            Token? t;
            do
            {
                t = this.lexer.NextToken();
            }
            while (t?.Type == TokenType.Comment);

            if (t == null)
            {
                break;
            }

            pulled.Add(t);
            if (i == offset)
            {
                result = t;
            }
        }

        for (int i = pulled.Count - 1; i >= 0; i--)
        {
            this.lexer.PutBackToken(pulled[i]);
        }

        return result;
    }

    public void Advance()
    {
        if (this.aborted)
        {
            return;
        }

        this.currentToken = this.lexer.NextToken();

        // Skip comments
        while (!this.aborted && this.currentToken?.Type == TokenType.Comment)
        {
            this.currentToken = this.lexer.NextToken();
        }

        if (this.currentToken != null)
        {
            this.lastNonNullToken = this.currentToken;
        }
    }

    public Token Consume(TokenType type, string message)
    {
        if (this.aborted)
        {
            return this.CreateMissingToken(type, this.GetErrorPosition());
        }

        if (this.Check(type))
        {
            var token = this.Current;
            if (token != null)
            {
                this.Advance();
                return token;
            }

            // Current unexpectedly null
            this.AbortUnexpectedEnd();
            return this.CreateMissingToken(type, this.GetErrorPosition());
        }

        // Report error and create a synthetic (missing) token to allow recovery
        this.ReportError(message, this.currentToken ?? this.lastNonNullToken, expected: type);
        return this.CreateMissingToken(type, (this.currentToken ?? this.lastNonNullToken)?.Start ?? 0);
    }

    public void Initialize()
    {
        if (!this.initialized)
        {
            this.Advance();
            this.initialized = true;
        }
    }

    // Parse expression with operator precedence
    public Expression ParseExpression()
    {
        this.Initialize();
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        return this.ParseOr();
    }

    public Expression ParsePrimaryExpression()
    {
        this.Initialize();
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        return this.ParsePrimary();
    }

    private bool Check(TokenType type)
    {
        this.Initialize();
        if (this.aborted || this.IsAtEnd)
        {
            return false;
        }

        if (this.currentToken == null)
        {
            this.AbortUnexpectedEnd();
            return false;
        }

        return this.currentToken.Type == type;
    }

    // Logical OR (lowest precedence)
    private Expression ParseOr()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var left = this.ParseAnd();

        while (!this.aborted && this.Check(TokenType.Or))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            var right = this.ParseAnd();
            left = new BinaryOperatorExpression(left, opToken, right);
        }

        return left;
    }

    // Logical AND
    private Expression ParseAnd()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var left = this.ParseXor();

        while (!this.aborted && this.Check(TokenType.And))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            var right = this.ParseXor();
            left = new BinaryOperatorExpression(left, opToken, right);
        }

        return left;
    }

    // Logical XOR
    private Expression ParseXor()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var left = this.ParseEquality();

        while (!this.aborted && this.Check(TokenType.Xor))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            var right = this.ParseEquality();
            left = new BinaryOperatorExpression(left, opToken, right);
        }

        return left;
    }

    // Equality operators (==, !=)
    private Expression ParseEquality()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var left = this.ParseComparison();

        while (!this.aborted && (this.Check(TokenType.Equal) || this.Check(TokenType.NotEqual)))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            var right = this.ParseComparison();
            left = new BinaryOperatorExpression(left, opToken, right);
        }

        return left;
    }

    // Comparison operators (<, >, <=, >=)
    private Expression ParseComparison()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var left = this.ParseAddition();

        while (!this.aborted &&
               (this.Check(TokenType.LessThan) ||
                this.Check(TokenType.GreaterThan) ||
                this.Check(TokenType.LessThanOrEqual) ||
                this.Check(TokenType.GreaterThanOrEqual)))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            var right = this.ParseAddition();
            left = new BinaryOperatorExpression(left, opToken, right);
        }

        return left;
    }

    // Addition and subtraction
    private Expression ParseAddition()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var left = this.ParseMultiplication();

        while (!this.aborted && (this.Check(TokenType.Plus) || this.Check(TokenType.Minus)))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            var right = this.ParseMultiplication();
            left = new BinaryOperatorExpression(left, opToken, right);
        }

        return left;
    }

    // Multiplication, division, and modulo
    private Expression ParseMultiplication()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var left = this.ParsePower();

        while (!this.aborted &&
               (this.Check(TokenType.Multiply) ||
                this.Check(TokenType.Divide) ||
                this.Check(TokenType.Mod)))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            var right = this.ParsePower();
            left = new BinaryOperatorExpression(left, opToken, right);
        }

        return left;
    }

    // Power operator (right associative)
    private Expression ParsePower()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var left = this.ParseUnary();

        if (!this.aborted && this.Check(TokenType.Pow))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();

            // Right associative: 2**3**4 = 2**(3**4)
            var right = this.ParsePower();
            return new BinaryOperatorExpression(left, opToken, right);
        }

        return left;
    }

    // Unary operators (!, -, +)
    private Expression ParseUnary()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        if (this.Check(TokenType.Not) ||
            this.Check(TokenType.Minus) ||
            this.Check(TokenType.Plus))
        {
            var opToken = this.Current;
            if (opToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            var expr = this.ParseUnary();
            return new UnaryOperatorExpression(opToken, expr);
        }

        return this.ParsePrimary();
    }

    // Primary expressions (literals, variables, parentheses)
    private Expression ParsePrimary()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        // Handle parentheses - may contain an expression or a command
        if (this.Check(TokenType.OpenParenthesis))
        {
            var openToken = this.Current;
            if (openToken == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();

            // Check if this looks like a command invocation (identifier that's not a boolean/variable)
            if (this.Check(TokenType.Identifier) && this.currentToken != null)
            {
                var identToken = this.currentToken;
                var value = identToken.Value;

                // Skip booleans and variables - these should be parsed as expressions
                bool isBooleanOrVariable = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                                           value.StartsWith("$");

                if (!isBooleanOrVariable)
                {
                    // This could be a command - peek ahead to see if there are arguments
                    // before the closing paren or if this is a simple expression
                    var commandExpr = this.TryParseCommandExpression(identToken);
                    if (commandExpr != null)
                    {
                        var closeToken = this.Consume(TokenType.CloseParenthesis, MessageService.GetString("expression_error_expected_close_paren"));
                        return new ParensExpression(openToken, commandExpr, closeToken);
                    }
                }
            }

            // Regular expression parsing
            var expr = this.ParseOr();
            var closeTokenExpr = this.Consume(TokenType.CloseParenthesis, MessageService.GetString("expression_error_expected_close_paren"));
            return new ParensExpression(openToken, expr, closeTokenExpr);
        }

        // Handle JSON objects
        if (this.Check(TokenType.OpenBrace))
        {
            if (this.Current == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            return this.ParseJsonExpression();
        }

        // Handle JSON arrays
        if (this.Check(TokenType.OpenBracket))
        {
            if (this.Current == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            return this.ParseJsonArray();
        }

        // Handle numbers
        if (this.Check(TokenType.Number))
        {
            var token = this.Current;
            if (token == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();

            if (int.TryParse(token.Value, out int intValue))
            {
                return new ConstantExpression(token, new ShellNumber(intValue));
            }

            this.ReportError(MessageService.GetArgsString("expression_error_invalid_number", "value", token.Value), token);
            return new ErrorExpression(token.Start, token.Length);
        }

        if (this.Check(TokenType.Decimal))
        {
            var token = this.Current;
            if (token == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();

            if (double.TryParse(token.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double doubleValue))
            {
                return new ConstantExpression(token, new ShellDecimal(doubleValue));
            }

            this.ReportError(MessageService.GetArgsString("expression_error_invalid_number", "value", token.Value), token);
            return new ErrorExpression(token.Start, token.Length);
        }

        // Handle strings
        if (this.Check(TokenType.String))
        {
            var token = this.Current;
            if (token == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            return new ConstantExpression(token, new ShellText(token.Value));
        }

        if (this.Check(TokenType.InterpolatedString))
        {
            var token = this.Current;
            if (token == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();
            return this.ParseInterpolatedStringExpression(token);
        }

        // Handle identifiers (variables, booleans, etc.)
        if (this.Check(TokenType.Identifier))
        {
            var token = this.Current;
            if (token == null)
            {
                this.AbortUnexpectedEnd();
                return this.CreateAbortExpression();
            }

            this.Advance();

            // Check for boolean literals
            if (string.Equals(token.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return new ConstantExpression(token, new ShellBool(true));
            }

            if (string.Equals(token.Value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return new ConstantExpression(token, new ShellBool(false));
            }

            // Check for variables
            if (token.Value.StartsWith("$") && token.Value.Length > 1)
            {
                var varValue = token.Value[1..]; // Remove leading $

                // JSON path on piped result: $.name, $.[0], $.users[0].name
                if (varValue.StartsWith("."))
                {
                    return new JSonPathExpression(token, varValue);
                }

                // Variable with property access: $script.name
                if (varValue.IndexOf('.') > 0)
                {
                    return new JSonPathExpression(token, varValue);
                }

                // Variable with array access: $items[0]
                if (varValue.IndexOf('[') > 0)
                {
                    return new JSonPathExpression(token, varValue);
                }

                return new VariableExpression(token, varValue);
            }

            return new ConstantExpression(token, new ShellIdentifier(token.Value));
        }

        if (this.IsAtEnd)
        {
            // Unexpected end of input
            this.AbortUnexpectedEnd();
            return this.CreateAbortExpression();
        }

        // Unexpected token
        var unexpected = this.currentToken!;
        this.ReportError(
            MessageService.GetArgsString("expression_error_unexpected_token", "type", unexpected.Type.ToString(), "value", unexpected.Value),
            unexpected);
        this.Advance(); // Attempt recovery by consuming the unexpected token
        return new ErrorExpression(unexpected.Start, unexpected.Length);
    }

    private InterpolatedStringExpression ParseInterpolatedStringExpression(Token token)
    {
        var expressions = new List<Expression>();
        var content = token.Value; // The content without the quotes
        var position = 0;

        while (position < content.Length)
        {
            // Find the next interpolation
            var dollarIndex = content.IndexOf('$', position);

            if (dollarIndex == -1)
            {
                // No more interpolations, add the rest as a constant string
                if (position < content.Length)
                {
                    var remaining = content.Substring(position);
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        expressions.Add(new ConstantExpression(token, new ShellText(remaining)));
                    }
                }

                break;
            }

            // Check if the dollar sign is escaped with a backslash
            if (dollarIndex > 0 && content[dollarIndex - 1] == '\\')
            {
                // Add text before the escaped dollar (excluding the backslash)
                if (dollarIndex - 1 > position)
                {
                    var textBefore = content.Substring(position, dollarIndex - 1 - position);
                    expressions.Add(new ConstantExpression(token, new ShellText(textBefore)));
                }

                // Add the literal dollar sign
                expressions.Add(new ConstantExpression(token, new ShellText("$")));

                // Continue after the dollar sign
                position = dollarIndex + 1;
                continue;
            }

            // Add text before the interpolation as a constant
            if (dollarIndex > position)
            {
                var textBefore = content.Substring(position, dollarIndex - position);
                expressions.Add(new ConstantExpression(token, new ShellText(textBefore)));
            }

            // Check what follows the $
            position = dollarIndex + 1;

            if (position >= content.Length)
            {
                // $ at the end of string
                expressions.Add(new ConstantExpression(token, new ShellText("$")));
                break;
            }

            if (content[position] == '(')
            {
                // Expression interpolation $(EXPR)
                position++; // Skip opening paren
                var startExprPos = position;
                var parenCount = 1;

                // Find matching closing paren
                while (position < content.Length && parenCount > 0)
                {
                    if (content[position] == '(')
                    {
                        parenCount++;
                    }
                    else if (content[position] == ')')
                    {
                        parenCount--;
                    }

                    position++;
                }

                if (parenCount != 0)
                {
                    this.lexer.Errors.Add(ParseError.CreateError(token.Start, token.Length, "Unmatched parentheses in interpolated expression"));

                    // Treat entire remainder as literal
                    var remaining = content.Substring(startExprPos - 2); // include $"(
                    expressions.Add(new ConstantExpression(token, new ShellText(remaining)));
                    break;
                }

                // Extract the expression content (without the closing paren)
                var exprContent = content.Substring(startExprPos, position - startExprPos - 1);

                // Parse the expression
                if (!string.IsNullOrWhiteSpace(exprContent))
                {
                    var exprLexer = new Lexer(exprContent);
                    var exprParser = new ExpressionParser(exprLexer);
                    var expr = exprParser.ParseExpression();

                    // Merge nested errors
                    if (exprLexer.Errors.Count > 0)
                    {
                        this.lexer.Errors.AddRange(exprLexer.Errors);
                    }

                    expressions.Add(expr);
                }
            }
            else if (char.IsLetter(content[position]) || content[position] == '_')
            {
                // Variable interpolation $VAR_NAME or $VAR.prop or $VAR[0]
                var startVarPos = position;

                // Read variable name (letters, digits, underscores, '-', '.', '[', ']')
                while (position < content.Length &&
                       (char.IsLetterOrDigit(content[position]) ||
                        content[position] == '_' ||
                        content[position] == '-' ||
                        content[position] == '.' ||
                        content[position] == '[' ||
                        content[position] == ']'))
                {
                    position++;
                }

                var varName = content.Substring(startVarPos, position - startVarPos);
                if (!string.IsNullOrEmpty(varName))
                {
                    // Check if it contains property access or array access
                    if (varName.Contains('.') || varName.Contains('['))
                    {
                        expressions.Add(new JSonPathExpression(token, varName));
                    }
                    else
                    {
                        expressions.Add(new VariableExpression(token, varName));
                    }
                }
            }
            else if (char.IsDigit(content[position]))
            {
                // Positional parameter interpolation $0, $1, etc.
                var startVarPos = position;

                // Read positional parameter (digits only)
                while (position < content.Length && char.IsDigit(content[position]))
                {
                    position++;
                }

                var varName = content.Substring(startVarPos, position - startVarPos);
                if (!string.IsNullOrEmpty(varName))
                {
                    expressions.Add(new VariableExpression(token, varName));
                }
            }
            else
            {
                // $ followed by something else, treat as literal $
                expressions.Add(new ConstantExpression(token, new ShellText("$")));
            }
        }

        return new InterpolatedStringExpression(token, expressions);
    }

    private JsonExpression ParseJsonExpression()
    {
        if (this.aborted)
        {
            return new JsonExpression(this.CreateMissingToken(TokenType.OpenBrace, this.GetErrorPosition()), this.CreateMissingToken(TokenType.CloseBrace, this.GetErrorPosition()), new Dictionary<ShellObject, Expression>());
        }

        var lbrace = this.Current;
        if (lbrace == null)
        {
            this.AbortUnexpectedEnd();
            var syntheticStart = this.CreateMissingToken(TokenType.OpenBrace, this.GetErrorPosition());
            var syntheticEnd = this.CreateMissingToken(TokenType.CloseBrace, this.GetErrorPosition());
            return new JsonExpression(syntheticStart, syntheticEnd, new Dictionary<ShellObject, Expression>());
        }

        this.Advance(); // consume '{'

        var properties = new Dictionary<ShellObject, Expression>();

        void SkipTrivia()
        {
            while (!this.IsAtEnd &&
                   this.currentToken != null &&
                   (this.currentToken.Type == TokenType.Semicolon ||
                    this.currentToken.Type == TokenType.Eol ||
                    this.currentToken.Type == TokenType.Comment))
            {
                this.Advance();
            }
        }

        SkipTrivia();

        // Empty object?
        if (!this.IsAtEnd && this.currentToken?.Type == TokenType.CloseBrace)
        {
            var rbrace = this.currentToken!;
            this.Advance(); // consume '}'
            return new JsonExpression(lbrace, rbrace, properties);
        }

        while (!this.IsAtEnd)
        {
            SkipTrivia();

            if (this.IsAtEnd)
            {
                break;
            }

            // Check for closing brace
            if (this.currentToken?.Type == TokenType.CloseBrace)
            {
                var rbrace = this.currentToken!;
                this.Advance(); // consume '}'
                return new JsonExpression(lbrace, rbrace, properties);
            }

            // Parse property name (key)
            ShellObject? key = null;
            var keyToken = this.currentToken;
            if (keyToken == null)
            {
                this.AbortUnexpectedEnd();
                break;
            }

            if (keyToken.Type == TokenType.String ||
                keyToken.Type == TokenType.Identifier ||
                keyToken.Type == TokenType.Number)
            {
                key = new ShellText(keyToken.Value);
                this.Advance();
            }
            else
            {
                this.ReportError(
                    MessageService.GetArgsString("expression_error_expected_property_name", "token", keyToken.Type.ToString()),
                    keyToken);

                // Attempt recovery: skip token
                this.Advance();
                continue;
            }

            SkipTrivia();

            // Expect colon
            if (this.IsAtEnd || this.currentToken?.Type != TokenType.Colon)
            {
                var gotType = this.IsAtEnd ? "EndOfInput" : this.currentToken?.Type.ToString() ?? "null";
                var gotValue = this.IsAtEnd ? string.Empty : this.currentToken?.Value ?? string.Empty;
                this.ReportError(
                    MessageService.GetArgsString("expression_error_unexpected_token", "type", gotType, "value", gotValue),
                    this.currentToken ?? keyToken);
                if (!this.IsAtEnd)
                {
                    this.Advance(); // try to move on
                }

                // Skip value parsing; keep partial
                continue;
            }

            this.Advance(); // consume ':'

            SkipTrivia();

            if (this.aborted)
            {
                break;
            }

            // Parse property value as a full expression
            var value = this.ParseOr();

            // Add to properties if key valid
            if (key != null)
            {
                properties[key] = value;
            }

            SkipTrivia();

            // Check for comma or closing brace
            if (!this.IsAtEnd)
            {
                if (this.currentToken?.Type == TokenType.Comma)
                {
                    this.Advance(); // consume ','
                    SkipTrivia();

                    // Allow trailing comma
                    if (!this.IsAtEnd && this.currentToken?.Type == TokenType.CloseBrace)
                    {
                        var rbrace = this.currentToken!;
                        this.Advance(); // consume '}'
                        return new JsonExpression(lbrace, rbrace, properties);
                    }

                    continue;
                }
                else if (this.currentToken?.Type == TokenType.CloseBrace)
                {
                    var rbrace = this.currentToken!;
                    this.Advance(); // consume '}'
                    return new JsonExpression(lbrace, rbrace, properties);
                }
                else
                {
                    if (this.currentToken != null)
                    {
                        this.ReportError(
                            MessageService.GetArgsString("expression_error_expected_comma_or_brace", "token", this.currentToken.Type.ToString()),
                            this.currentToken);
                    }

                    // Recovery: skip unexpected token
                    this.Advance();
                    continue;
                }
            }
        }

        if (this.aborted)
        {
            var syntheticAbort = this.CreateMissingToken(TokenType.CloseBrace, this.GetErrorPosition());
            return new JsonExpression(lbrace, syntheticAbort, properties);
        }

        // Missing closing brace
        this.ReportError(MessageService.GetString("expression_error_unmatched_braces"), lbrace);
        var synthetic = this.CreateMissingToken(TokenType.CloseBrace, lbrace.Start + lbrace.Length);
        return new JsonExpression(lbrace, synthetic, properties);
    }

    private void SkipWhitespace()
    {
        while (!this.IsAtEnd &&
               this.Current != null &&
               (this.Current.Type == TokenType.Semicolon ||
                this.Current.Type == TokenType.Eol ||
                this.Current.Type == TokenType.Comment))
        {
            this.Advance();
        }
    }

    private Expression ParseJsonArray()
    {
        if (this.aborted)
        {
            return this.CreateAbortExpression();
        }

        var lbracket = this.Current;
        if (lbracket == null)
        {
            this.AbortUnexpectedEnd();
            return this.CreateAbortExpression();
        }

        this.Advance(); // consume '['

        var elements = new List<Expression>();

        this.SkipWhitespace();

        // Empty array?
        if (!this.IsAtEnd && this.Current != null && this.Current.Type == TokenType.CloseBracket)
        {
            var rbracketEmpty = this.Current;
            this.Advance();
            return new JsonArrayExpression(lbracket, rbracketEmpty!, elements);
        }

        while (!this.IsAtEnd)
        {
            this.SkipWhitespace();

            // Check if we've reached the closing bracket
            if (!this.IsAtEnd && this.Current != null && this.Current.Type == TokenType.CloseBracket)
            {
                var rbracket = this.Current;
                this.Advance(); // consume ']'
                return new JsonArrayExpression(lbracket, rbracket!, elements);
            }

            if (this.aborted)
            {
                break;
            }

            // Parse next element as a full expression
            var expr = this.ParseOr();
            elements.Add(expr);

            this.SkipWhitespace();

            if (!this.IsAtEnd && this.Check(TokenType.Comma))
            {
                this.Advance(); // consume ',' and continue with next element
                this.SkipWhitespace();

                // Handle trailing comma before closing bracket
                if (!this.IsAtEnd && this.Current != null && this.Current.Type == TokenType.CloseBracket)
                {
                    var rbracket = this.Current;
                    this.Advance(); // consume ']'
                    return new JsonArrayExpression(lbracket, rbracket!, elements);
                }

                continue;
            }

            if (!this.IsAtEnd && this.Check(TokenType.CloseBracket))
            {
                var rbracket = this.Current;
                if (rbracket == null)
                {
                    this.AbortUnexpectedEnd();
                    return new JsonArrayExpression(lbracket, this.CreateMissingToken(TokenType.CloseBracket, this.GetErrorPosition()), elements);
                }

                this.Advance(); // consume ']'
                return new JsonArrayExpression(lbracket, rbracket, elements);
            }

            if (this.IsAtEnd)
            {
                break;
            }

            if (this.Current == null)
            {
                this.AbortUnexpectedEnd();
                break;
            }

            // Neither ',' nor ']' followed the element
            this.ReportError(
                MessageService.GetArgsString("expression_error_expected_comma_or_bracket", "token", this.Current.Type.ToString()),
                this.Current);
            this.Advance(); // Attempt recovery
        }

        if (this.aborted)
        {
            return new JsonArrayExpression(lbracket, this.CreateMissingToken(TokenType.CloseBracket, this.GetErrorPosition()), elements);
        }

        // Missing closing bracket
        this.ReportError(MessageService.GetString("expression_error_unmatched_brackets"), lbracket);
        var synthetic = this.CreateMissingToken(TokenType.CloseBracket, lbracket.Start + lbracket.Length);
        return new JsonArrayExpression(lbracket, synthetic, elements);
    }

    private void ReportError(string message, Token? token, TokenType? expected = null, int? position = null)
    {
        int start = position ?? token?.Start ?? (this.lastNonNullToken?.Start ?? 0);
        int length = token?.Length ?? 0;
        this.lexer.Errors.Add(ParseError.CreateError(start, length, message));
    }

    private void AbortUnexpectedEnd()
    {
        if (this.aborted)
        {
            return;
        }

        int pos = this.GetErrorPosition();
        this.ReportError(MessageService.GetString("expression_error_unexpected_end"), null, position: pos);
        this.aborted = true;
    }

    private int GetErrorPosition()
    {
        return (this.lastNonNullToken?.Start ?? 0) + (this.lastNonNullToken?.Length ?? 0);
    }

    private Expression CreateAbortExpression()
    {
        int pos = this.GetErrorPosition();
        return new ErrorExpression(pos, 0);
    }

    private Token CreateMissingToken(TokenType type, int position)
    {
        return new Token(type, string.Empty, position, 0);
    }

    /// <summary>
    /// Attempts to parse a command expression inside parentheses.
    /// Returns a CommandExpression if the identifier is followed by arguments before the closing paren,
    /// or null if it's just a simple identifier (should be parsed as a regular expression).
    /// </summary>
    private CommandExpression? TryParseCommandExpression(Token commandToken)
    {
        // Create the command expression
        var commandExpr = new CommandExpression(commandToken);

        // Consume the command token
        this.Advance();

        // Check if there's anything before the closing paren
        // If we immediately see ), this is just an identifier, not a command
        if (this.Check(TokenType.CloseParenthesis))
        {
            // Just a bare identifier like (myvar) - could be a command with no args
            // We'll treat it as a command if it's followed immediately by )
            return commandExpr;
        }

        // Parse command arguments until we hit the closing paren
        while (!this.IsAtEnd && !this.Check(TokenType.CloseParenthesis))
        {
            if (this.aborted)
            {
                break;
            }

            // Parse argument based on token type
            var arg = this.ParseCommandArgument();
            if (arg != null)
            {
                commandExpr.Arguments.Add(arg);
            }
            else
            {
                // If we couldn't parse an argument, break to avoid infinite loop
                break;
            }
        }

        return commandExpr;
    }

    /// <summary>
    /// Parses a single command argument (for use within command expressions).
    /// </summary>
    private Expression? ParseCommandArgument()
    {
        if (this.IsAtEnd || this.Check(TokenType.CloseParenthesis))
        {
            return null;
        }

        var commandWordParser = this.CreateCommandShellWordParser();
        if (this.Check(TokenType.Minus) && commandWordParser.IsCommandOptionStart())
        {
            var optionStartToken = this.currentToken!;
            this.Advance();

            // Optional double dash
            if (!this.IsAtEnd && this.Check(TokenType.Minus))
            {
                this.Advance();
            }

            // Must have an identifier for the option name
            if (this.IsAtEnd || this.currentToken == null || this.currentToken.Type != TokenType.Identifier)
            {
                return new ConstantExpression(optionStartToken, new ShellIdentifier("-"));
            }

            var optionNameToken = this.currentToken;
            this.Advance();

            Expression? optionValue = null;
            if (!this.IsAtEnd && this.currentToken != null &&
                (this.currentToken.Type == TokenType.Colon || this.currentToken.Type == TokenType.Assignment))
            {
                this.Advance(); // consume ':' or '='
                optionValue = commandWordParser.ParseShellWord();
            }

            return new CommandOption(optionStartToken, optionNameToken, optionValue);
        }

        return commandWordParser.ParseShellWord();
    }

    private CommandShellWordParser CreateCommandShellWordParser()
        => new(
            () => this.currentToken,
            () => this.IsAtEnd,
            () => this.Peek(),
            offset => this.PeekAt(offset),
            () => this.Advance(),
            () => this.ParsePrimary(),
            token => token.Type == TokenType.CloseParenthesis);
}