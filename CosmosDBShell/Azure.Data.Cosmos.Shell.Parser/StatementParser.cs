// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

internal class StatementParser
{
    private readonly ExpressionParser expressionParser;
    private readonly Lexer lexer;

    public StatementParser(string text)
        : this(new Lexer(text))
    {
    }

    public StatementParser(Lexer lexer)
    {
        this.lexer = lexer;
        this.expressionParser = new ExpressionParser(lexer);
        this.expressionParser.Initialize();
    }

    public List<Token> Comments { get => this.lexer.Comments; }

    public ErrorList Errors { get => this.lexer.Errors; }

    public List<Statement> ParseStatements()
    {
        var statements = new List<Statement>();

        while (!this.expressionParser.IsAtEnd)
        {
            Statement? statement = null;
            try
            {
                statement = this.ParseStatement();
            }
            catch (InvalidOperationException ex)
            {
                this.ReportError(ex.Message, this.expressionParser.Current);
                this.Synchronize();
            }

            if (statement != null)
            {
                statements.Add(statement);
            }
            else
            {
                // Forward progress safeguard: if we produced no statement
                // and did not reach end, advance one token to avoid a tight loop.
                if (!this.expressionParser.IsAtEnd)
                {
                    this.expressionParser.Advance();
                }
            }

            while (!this.expressionParser.IsAtEnd &&
                   this.expressionParser.Current != null &&
                   (this.expressionParser.Current.Type == TokenType.Semicolon ||
                    this.expressionParser.Current.Type == TokenType.Eol))
            {
                this.expressionParser.Advance();
            }
        }

        return statements;
    }

    public Statement? ParseStatement()
    {
        if (this.expressionParser.IsAtEnd)
        {
            return null;
        }

        Statement? first;
        try
        {
            first = this.ParseSingleStatement();
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            this.Synchronize();
            return null;
        }

        if (first == null)
        {
            return null;
        }

        var segments = new List<Statement> { first };

        while (!this.expressionParser.IsAtEnd &&
               this.expressionParser.Current != null &&
               this.expressionParser.Current.Type == TokenType.Pipe)
        {
            var pipeToken = this.expressionParser.Current;
            this.expressionParser.Advance();

            Statement? next = null;
            try
            {
                next = this.ParseSingleStatement();
                if (next == null)
                {
                    this.ReportError(
                        MessageService.GetString("statement_error_expected_after_pipe"),
                        pipeToken);
                }
            }
            catch (InvalidOperationException ex)
            {
                this.ReportError(ex.Message, this.expressionParser.Current);
                this.Synchronize();
            }

            if (next != null)
            {
                segments.Add(next);
            }
            else
            {
                break;
            }
        }

        if (segments.Count > 1)
        {
            return new PipeStatement(segments);
        }

        return segments[0];
    }

    private static string RedirectLabel(Token redirectToken)
        => redirectToken.Type switch
        {
            TokenType.RedirectOutput or TokenType.RedirectAppendOutput => "out>",
            _ => "err>",
        };

    private static bool IsBinaryOperator(TokenType type)
        => type switch
        {
            TokenType.Plus => true,

            // Note: Minus is NOT included because in command context, - starts an option
            TokenType.Multiply => true,
            TokenType.Divide => true,
            TokenType.Mod => true,
            TokenType.Pow => true,
            TokenType.Equal => true,
            TokenType.NotEqual => true,
            TokenType.LessThan => true,
            TokenType.GreaterThan => true,
            TokenType.LessThanOrEqual => true,
            TokenType.GreaterThanOrEqual => true,
            TokenType.And => true,
            TokenType.Or => true,
            TokenType.Xor => true,
            _ => false,
        };

    private void SkipWs()
    {
        while (!this.expressionParser.IsAtEnd &&
               this.expressionParser.Current != null &&
               (this.expressionParser.Current.Type == TokenType.Eol ||
                this.expressionParser.Current.Type == TokenType.Comment))
        {
            this.expressionParser.Advance();
        }
    }

    private Statement? ParseSingleStatement()
    {
        if (this.expressionParser.IsAtEnd)
        {
            return null;
        }

        this.SkipWs();

        var current = this.expressionParser.Current;
        if (current == null)
        {
            this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
            return null;
        }

        // Fix: Handle stray/unexpected closing brace early to avoid infinite loop.
        if (current.Type == TokenType.CloseBrace)
        {
            this.ReportError(MessageService.GetString("statement_error_unexpected_close_brace") ?? "Unexpected '}'", current);
            this.expressionParser.Advance();
            return null;
        }

        if (current.Type == TokenType.Identifier)
        {
            return current.Value.ToLowerInvariant() switch
            {
                "if" => this.Safe(() => this.ParseIfStatement()),
                "while" => this.Safe(() => this.ParseWhileStatement()),
                "for" => this.Safe(() => this.ParseForStatement()),
                "do" => this.Safe(() => this.ParseDoWhileStatement()),
                "loop" => this.Safe(() => this.ParseLoopStatement()),
                "def" => this.Safe(() => this.ParseDefStatement()),
                "return" => this.Safe(() => this.ParseReturnStatement()),
                "break" => this.Safe(() => this.ParseBreakStatement()),
                "continue" => this.Safe(() => this.ParseContinueStatement()),
                "exec" => this.Safe(() => this.ParseExecStatement()),
                _ => this.Safe(() => this.ParseAssignmentOrCommand()),
            };
        }

        if (current.Type == TokenType.OpenBrace)
        {
            return this.Safe(() => this.ParseBlockStatement());
        }

        if (current.Type == TokenType.Identifier && current.Value.StartsWith('$'))
        {
            return this.Safe(() => this.ParseAssignmentOrCommand());
        }

        return this.Safe(() => this.ParseCommandStatement());
    }

    private IfStatement? ParseIfStatement()
    {
        try
        {
            var ifToken = this.expressionParser.Current;
            if (ifToken == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_if"));
            var condition = this.expressionParser.ParseExpression();

            var thenStatement = this.ParseStatement();
            if (thenStatement == null)
            {
                this.ReportError(MessageService.GetString("statement_error_expected_after_if"), this.expressionParser.Current);
                return null;
            }

            this.SkipWs();

            Token? elseToken = null;
            Statement? elseStatement = null;
            if (!this.expressionParser.IsAtEnd &&
                this.expressionParser.Current != null &&
                this.expressionParser.Current.Type == TokenType.Identifier &&
                this.expressionParser.Current.Value.Equals("else", StringComparison.InvariantCultureIgnoreCase))
            {
                elseToken = this.expressionParser.Current;
                this.expressionParser.Advance();
                elseStatement = this.ParseStatement();
            }

            return new IfStatement(ifToken, condition, thenStatement, elseToken, elseStatement);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private WhileStatement? ParseWhileStatement()
    {
        try
        {
            var whileToken = this.expressionParser.Current;
            if (whileToken == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_while"));
            var condition = this.expressionParser.ParseExpression();
            var statement = this.ParseStatement();
            if (statement == null)
            {
                this.ReportError(MessageService.GetString("statement_error_expected_after_while"), this.expressionParser.Current);
                return null;
            }

            return new WhileStatement(whileToken, condition, statement);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private ForStatement? ParseForStatement()
    {
        try
        {
            var forToken = this.expressionParser.Current;
            if (forToken == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_for"));

            if (this.expressionParser.Current == null ||
                this.expressionParser.Current.Type != TokenType.Identifier ||
                !this.expressionParser.Current.Value.StartsWith('$'))
            {
                this.ReportError(MessageService.GetString("statement_error_expected_variable_after_for"), this.expressionParser.Current);
                return null;
            }

            var variableName = this.expressionParser.Current;
            this.expressionParser.Advance();

            if (this.expressionParser.Current == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            var inToken = this.expressionParser.Current;
            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_in"));

            var collection = this.expressionParser.ParseExpression();
            var statement = this.ParseStatement();
            if (statement == null)
            {
                this.ReportError(MessageService.GetString("statement_error_expected_after_for_collection"), this.expressionParser.Current);
                return null;
            }

            return new ForStatement(forToken, variableName, inToken, collection, statement);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private DoWhileStatement? ParseDoWhileStatement()
    {
        try
        {
            var doToken = this.expressionParser.Current;
            if (doToken == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_do"));

            var statement = this.ParseStatement();
            if (statement == null)
            {
                this.ReportError(MessageService.GetString("statement_error_expected_after_do"), this.expressionParser.Current);
                return null;
            }

            this.SkipWs();

            if (this.expressionParser.Current == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            var whileToken = this.expressionParser.Current;
            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_while"));

            var condition = this.expressionParser.ParseExpression();
            return new DoWhileStatement(doToken, statement, whileToken, condition);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private LoopStatement? ParseLoopStatement()
    {
        try
        {
            var loopToken = this.expressionParser.Current;
            if (loopToken == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_loop"));
            var statement = this.ParseStatement();
            if (statement == null)
            {
                this.ReportError(MessageService.GetString("statement_error_expected_after_loop"), this.expressionParser.Current);
                return null;
            }

            return new LoopStatement(loopToken, statement);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private ExecStatement? ParseExecStatement()
    {
        try
        {
            var execToken = this.expressionParser.Current;
            if (execToken == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, "Expected 'exec'");

            // Parse the command expression (first argument after 'exec')
            if (this.expressionParser.IsAtEnd || this.expressionParser.Current == null)
            {
                this.ReportError("Expected command expression after 'exec'", execToken);
                return null;
            }

            var commandExpression = this.expressionParser.ParsePrimaryExpression();

            // Parse additional arguments
            var arguments = new List<Expression>();
            while (!this.expressionParser.IsAtEnd &&
                   this.expressionParser.Current != null &&
                   this.expressionParser.Current.Type != TokenType.Semicolon &&
                   this.expressionParser.Current.Type != TokenType.Eol &&
                   this.expressionParser.Current.Type != TokenType.CloseBrace &&
                   this.expressionParser.Current.Type != TokenType.Pipe)
            {
                var arg = this.expressionParser.ParsePrimaryExpression();
                arguments.Add(arg);
            }

            return new ExecStatement(execToken, commandExpression, arguments);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private DefStatement? ParseDefStatement()
    {
        try
        {
            var defToken = this.expressionParser.Current;
            if (defToken == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_def"));

            if (this.expressionParser.Current == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            var nameToken = this.expressionParser.Current;
            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_function_name"));

            var parameters = new List<string>();

            // Check for parameter list with parentheses ()
            if (!this.expressionParser.IsAtEnd &&
                this.expressionParser.Current != null &&
                this.expressionParser.Current.Type == TokenType.OpenParenthesis)
            {
                this.expressionParser.Advance(); // consume '('

                // Parse parameters (comma-separated identifiers)
                var safetyCounter = 0;
                while (!this.expressionParser.IsAtEnd &&
                       this.expressionParser.Current != null &&
                       this.expressionParser.Current.Type != TokenType.CloseParenthesis)
                {
                    // Safety check to prevent infinite loop
                    if (++safetyCounter > 100)
                    {
                        this.ReportError("Too many parameters or malformed parameter list", this.expressionParser.Current);
                        break;
                    }

                    if (this.expressionParser.Current.Type == TokenType.Identifier)
                    {
                        var paramToken = this.expressionParser.Current;
                        parameters.Add(paramToken.Value.StartsWith('$') ? paramToken.Value[1..] : paramToken.Value);
                        this.expressionParser.Advance();

                        // Check for comma
                        if (!this.expressionParser.IsAtEnd &&
                            this.expressionParser.Current != null &&
                            this.expressionParser.Current.Type == TokenType.Comma)
                        {
                            this.expressionParser.Advance(); // consume ','
                            this.SkipWs(); // Skip whitespace after comma
                        }
                        else if (!this.expressionParser.IsAtEnd &&
                                 this.expressionParser.Current != null &&
                                 this.expressionParser.Current.Type != TokenType.CloseParenthesis)
                        {
                            // Expected comma or closing parenthesis
                            this.ReportError("Expected ',' or ')' after parameter", this.expressionParser.Current);
                            this.expressionParser.Advance(); // Try to recover
                        }
                    }
                    else if (this.expressionParser.Current.Type == TokenType.Comma)
                    {
                        // Unexpected comma without parameter
                        this.ReportError(MessageService.GetString("statement_error_expected_parameter_name"), this.expressionParser.Current);
                        this.expressionParser.Advance();
                    }
                    else
                    {
                        // Unexpected token in parameter list
                        this.ReportError(MessageService.GetString("statement_error_expected_parameter_name"), this.expressionParser.Current);

                        // Try to recover by advancing
                        this.expressionParser.Advance();
                    }
                }

                if (this.expressionParser.IsAtEnd)
                {
                    this.ReportError("Unexpected end of input in parameter list", null);
                    return null;
                }

                // Fixed: Check IsAtEnd and Current for null before accessing Current.Type
                if (!this.expressionParser.IsAtEnd &&
                    this.expressionParser.Current != null &&
                    this.expressionParser.Current.Type != TokenType.CloseParenthesis)
                {
                    this.ReportError(MessageService.GetString("statement_error_expected_close_parenthesis"), this.expressionParser.Current);

                    // Try to recover by looking for close parenthesis
                    while (!this.expressionParser.IsAtEnd &&
                           this.expressionParser.Current != null &&
                           this.expressionParser.Current.Type != TokenType.CloseParenthesis)
                    {
                        if (this.expressionParser.Current.Type == TokenType.OpenBrace ||
                            this.expressionParser.Current.Type == TokenType.Eol)
                        {
                            break; // Stop if we hit the function body
                        }

                        this.expressionParser.Advance();
                    }
                }

                if (!this.expressionParser.IsAtEnd &&
                    this.expressionParser.Current != null &&
                    this.expressionParser.Current.Type == TokenType.CloseParenthesis)
                {
                    this.expressionParser.Advance(); // consume ')'
                }
            }

            // Legacy bracket syntax [param1 param2]
            else if (!this.expressionParser.IsAtEnd &&
                     this.expressionParser.Current != null &&
                     this.expressionParser.Current.Type == TokenType.OpenBracket)
            {
                this.expressionParser.Advance();
                var safetyCounter = 0;
                while (!this.expressionParser.IsAtEnd &&
                       this.expressionParser.Current != null &&
                       this.expressionParser.Current.Type != TokenType.CloseBracket)
                {
                    // Safety check to prevent infinite loop
                    if (++safetyCounter > 100)
                    {
                        this.ReportError("Too many parameters or malformed parameter list", this.expressionParser.Current);
                        break;
                    }

                    if (this.expressionParser.Current.Type == TokenType.Identifier)
                    {
                        var paramToken = this.expressionParser.Current;
                        parameters.Add(paramToken.Value);
                        this.expressionParser.Advance();
                    }
                    else
                    {
                        this.ReportError(MessageService.GetString("statement_error_expected_parameter_name"), this.expressionParser.Current);
                        this.expressionParser.Advance(); // Try to recover
                    }
                }

                // Fixed: Check IsAtEnd and Current for null before accessing Current.Type
                if (!this.expressionParser.IsAtEnd &&
                    this.expressionParser.Current != null &&
                    this.expressionParser.Current.Type != TokenType.CloseBracket)
                {
                    this.ReportError(MessageService.GetString("statement_error_expected_close_bracket"), this.expressionParser.Current);
                }
                else if (!this.expressionParser.IsAtEnd && this.expressionParser.Current != null)
                {
                    this.expressionParser.Advance(); // consume ']'
                }
            }

            // Skip whitespace/EOL before body
            this.SkipWs();

            var body = this.ParseStatement();
            if (body == null)
            {
                this.ReportError(MessageService.GetString("statement_error_expected_after_function_def"), this.expressionParser.IsAtEnd ? null : this.expressionParser.Current);
                return null;
            }

            return new DefStatement(defToken, nameToken, parameters.ToArray(), body);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.IsAtEnd ? null : this.expressionParser.Current);
            return null;
        }
    }

    private ReturnStatement? ParseReturnStatement()
    {
        try
        {
            var token = this.expressionParser.Current;
            if (token == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_return"));

            Expression? value = null;
            if (!this.expressionParser.IsAtEnd &&
                this.expressionParser.Current != null &&
                this.expressionParser.Current.Type != TokenType.Semicolon &&
                this.expressionParser.Current.Type != TokenType.Eol)
            {
                value = this.expressionParser.ParseExpression();
            }

            return new ReturnStatement(token, value);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private BreakStatement? ParseBreakStatement()
    {
        try
        {
            var token = this.expressionParser.Current;
            if (token == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_break"));
            return new BreakStatement(token);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private ContinueStatement? ParseContinueStatement()
    {
        try
        {
            var token = this.expressionParser.Current;
            if (token == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_continue"));
            return new ContinueStatement(token);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private BlockStatement? ParseBlockStatement()
    {
        try
        {
            var openBrace = this.expressionParser.Current;
            if (openBrace == null)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            this.expressionParser.Consume(TokenType.OpenBrace, MessageService.GetString("statement_error_expected_open_brace"));

            while (!this.expressionParser.IsAtEnd &&
                   this.expressionParser.Current != null &&
                   (this.expressionParser.Current.Type == TokenType.Semicolon ||
                    this.expressionParser.Current.Type == TokenType.Comment ||
                    this.expressionParser.Current.Type == TokenType.Eol))
            {
                this.expressionParser.Advance();
            }

            var statements = new List<Statement>();
            while (!this.expressionParser.IsAtEnd &&
                   this.expressionParser.Current != null &&
                   this.expressionParser.Current.Type != TokenType.CloseBrace)
            {
                if (this.expressionParser.IsAtEnd || this.expressionParser.Current == null)
                {
                    break;
                }

                var stmt = this.ParseStatement();
                if (stmt != null)
                {
                    statements.Add(stmt);
                }

                while (!this.expressionParser.IsAtEnd &&
                       this.expressionParser.Current != null &&
                       (this.expressionParser.Current.Type == TokenType.Comment ||
                        this.expressionParser.Current.Type == TokenType.Semicolon ||
                        this.expressionParser.Current.Type == TokenType.Eol))
                {
                    this.expressionParser.Advance();
                }
            }

            if (this.expressionParser.Current == null || this.expressionParser.Current.Type != TokenType.CloseBrace)
            {
                this.ReportError(MessageService.GetString("statement_error_expected_close_brace"), this.expressionParser.Current);
                return null;
            }

            var closeBrace = this.expressionParser.Current;
            this.expressionParser.Advance(); // consume '}'

            return new BlockStatement(openBrace, closeBrace, statements);
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private Statement? ParseAssignmentOrCommand()
    {
        try
        {
            if (!this.expressionParser.IsAtEnd &&
                this.expressionParser.Current != null &&
                this.expressionParser.Current.Type == TokenType.Identifier)
            {
                var current = this.expressionParser.Current;
                this.expressionParser.Advance();

                if (!this.expressionParser.IsAtEnd &&
                    this.expressionParser.Current != null &&
                    this.expressionParser.Current.Type == TokenType.Assignment)
                {
                    var assignmentToken = this.expressionParser.Current;
                    this.expressionParser.Advance();
                    var value = this.expressionParser.ParseExpression();
                    var variable = new VariableExpression(current, current.Value.StartsWith('$') ? current.Value[1..] : current.Value);
                    return new AssignmentStatement(variable, assignmentToken, value);
                }
                else
                {
                    return this.ParseCommandStatement(current);
                }
            }

            return this.ParseCommandStatement();
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            return null;
        }
    }

    private CommandStatement? ParseCommandStatement(Token? optToken = null)
    {
        try
        {
            if (optToken == null && this.expressionParser.IsAtEnd)
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end_parsing_command"), this.expressionParser.Current);
                return null;
            }

            Token commandToken;
            if (optToken != null)
            {
                commandToken = optToken;
            }
            else if (this.expressionParser.Current != null)
            {
                commandToken = this.expressionParser.Consume(TokenType.Identifier, MessageService.GetString("statement_error_expected_command_name"));
            }
            else
            {
                this.ReportError(MessageService.GetString("statement_error_unexpected_end") ?? "Unexpected end of input", null);
                return null;
            }

            var command = new CommandStatement(commandToken);

            while (!this.expressionParser.IsAtEnd &&
                   this.expressionParser.Current != null &&
                   this.expressionParser.Current.Type != TokenType.Semicolon &&
                   this.expressionParser.Current.Type != TokenType.Eol &&
                   this.expressionParser.Current.Type != TokenType.CloseBrace &&
                   this.expressionParser.Current.Type != TokenType.Pipe &&
                   this.expressionParser.Current.Type != TokenType.RedirectOutput &&
                   this.expressionParser.Current.Type != TokenType.RedirectAppendOutput &&
                   this.expressionParser.Current.Type != TokenType.RedirectError &&
                   this.expressionParser.Current.Type != TokenType.RedirectAppendError)
            {
                if (this.expressionParser.Current.Type == TokenType.Minus)
                {
                    var optionStartToken = this.expressionParser.Current;
                    this.expressionParser.Advance();

                    bool doubleDash = false;
                    if (!this.expressionParser.IsAtEnd &&
                        this.expressionParser.Current != null &&
                        this.expressionParser.Current.Type == TokenType.Minus)
                    {
                        doubleDash = true;
                        this.expressionParser.Advance();
                    }

                    if (this.expressionParser.IsAtEnd ||
                        this.expressionParser.Current == null ||
                        this.expressionParser.Current.Type != TokenType.Identifier)
                    {
                        this.ReportError(
                            MessageService.GetArgsString("statement_error_expected_option_name", "prefix", doubleDash ? "--" : "-"),
                            this.expressionParser.Current);
                        break;
                    }

                    var optionNameToken = this.expressionParser.Current;
                    var optionName = optionNameToken.Value;
                    this.expressionParser.Advance();

                    Expression? optionValue = null;
                    if (!this.expressionParser.IsAtEnd &&
                        this.expressionParser.Current != null &&
                        (this.expressionParser.Current.Type == TokenType.Colon ||
                         this.expressionParser.Current.Type == TokenType.Assignment))
                    {
                        this.expressionParser.Advance();

                        if (!this.expressionParser.IsAtEnd &&
                            this.expressionParser.Current != null)
                        {
                            optionValue = this.expressionParser.ParsePrimaryExpression();

                            if (optionValue != null &&
                                !this.expressionParser.IsAtEnd &&
                                this.expressionParser.Current != null &&
                                IsBinaryOperator(this.expressionParser.Current.Type))
                            {
                                var opToken = this.expressionParser.Current;
                                this.expressionParser.Advance();
                                var right = this.expressionParser.ParseExpression();
                                optionValue = new BinaryOperatorExpression(optionValue, opToken, right);
                            }

                            if (optionValue == null)
                            {
                                this.ReportError(
                                    MessageService.GetArgsString("statement_error_invalid_option_value", "option", optionName),
                                    this.expressionParser.Current);
                            }
                        }
                        else
                        {
                            this.ReportError(
                                MessageService.GetArgsString("statement_error_invalid_option_value", "option", optionName),
                                this.expressionParser.Current);
                        }
                    }

                    command.Arguments.Add(new CommandOption(optionStartToken, optionNameToken, optionValue));
                }
                else
                {
                    // Parse primary expression first, then check if there's a binary operator following
                    // This handles cases like "text" + $var while still treating separate args individually
                    var arg = this.expressionParser.ParsePrimaryExpression();

                    // If followed by an operator, we need to parse a full binary expression
                    if (!this.expressionParser.IsAtEnd &&
                        this.expressionParser.Current != null &&
                        IsBinaryOperator(this.expressionParser.Current.Type))
                    {
                        // Re-parse as full expression - the operator will combine with what follows
                        var opToken = this.expressionParser.Current;
                        this.expressionParser.Advance();
                        var right = this.expressionParser.ParseExpression();
                        arg = new BinaryOperatorExpression(arg, opToken, right);
                    }

                    command.Arguments.Add(arg);
                }
            }

            while (!this.expressionParser.IsAtEnd &&
                   this.expressionParser.Current != null &&
                   (this.expressionParser.Current.Type == TokenType.RedirectOutput ||
                    this.expressionParser.Current.Type == TokenType.RedirectAppendOutput ||
                    this.expressionParser.Current.Type == TokenType.RedirectError ||
                    this.expressionParser.Current.Type == TokenType.RedirectAppendError))
            {
                var redirectToken = this.expressionParser.Current;
                this.expressionParser.Advance();

                if (this.expressionParser.IsAtEnd || this.expressionParser.Current == null)
                {
                    this.ReportError(
                        MessageService.GetArgsString("statement_error_expected_redirect_destination", "redirect", RedirectLabel(redirectToken)),
                        redirectToken);
                    break;
                }

                Token? destToken = null;
                if (this.expressionParser.Current.Type == TokenType.Identifier ||
                    this.expressionParser.Current.Type == TokenType.String)
                {
                    destToken = this.expressionParser.Current;
                    this.expressionParser.Advance();
                }
                else
                {
                    this.ReportError(
                        MessageService.GetArgsString("statement_error_invalid_redirect_destination", "redirect", RedirectLabel(redirectToken)),
                        this.expressionParser.Current);
                }

                if (redirectToken.Type == TokenType.RedirectOutput || redirectToken.Type == TokenType.RedirectAppendOutput)
                {
                    if (command.OutRedirectToken != null)
                    {
                        this.ReportError(MessageService.GetString("statement_error_duplicate_out_redirect"), redirectToken);
                    }
                    else
                    {
                        command.OutRedirectToken = redirectToken;
                        command.OutRedirectDestToken = destToken;
                    }
                }
                else
                {
                    if (command.ErrRedirectToken != null)
                    {
                        this.ReportError(MessageService.GetString("statement_error_duplicate_err_redirect"), redirectToken);
                    }
                    else
                    {
                        command.ErrRedirectToken = redirectToken;
                        command.ErrRedirectDestToken = destToken;
                    }
                }
            }

            return command;
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);

            // Advance to avoid being stuck on a bad token forever.
            if (!this.expressionParser.IsAtEnd)
            {
                this.expressionParser.Advance();
            }

            return null;
        }
    }

    private T? Safe<T>(Func<T?> action)
        where T : class
    {
        try
        {
            return action();
        }
        catch (InvalidOperationException ex)
        {
            this.ReportError(ex.Message, this.expressionParser.Current);
            if (!this.expressionParser.IsAtEnd)
            {
                this.expressionParser.Advance();
            }

            return null;
        }
    }

    private void ReportError(string message, Token? token)
    {
        var t = token ?? this.expressionParser.Current;
        if (t == null)
        {
            this.lexer.Errors.Add(new ParseError(0, 1, message, ErrorLevel.Error));
        }
        else
        {
            this.lexer.Errors.Add(new ParseError(t.Start, Math.Max(1, t.Length), message, ErrorLevel.Error));
        }
    }

    private void Synchronize()
    {
        var safety = 0;
        while (!this.expressionParser.IsAtEnd && safety++ < 256)
        {
            if (this.expressionParser.Current == null)
            {
                this.expressionParser.Advance();
                continue;
            }

            var tt = this.expressionParser.Current.Type;
            if (tt == TokenType.Semicolon ||
                tt == TokenType.Eol ||
                tt == TokenType.Pipe ||
                tt == TokenType.CloseBrace)
            {
                this.expressionParser.Advance();
                break;
            }

            this.expressionParser.Advance();
        }
    }
}