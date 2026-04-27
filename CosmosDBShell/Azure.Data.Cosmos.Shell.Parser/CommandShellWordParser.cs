// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

internal sealed class CommandShellWordParser
{
    private readonly Func<Token?> current;
    private readonly Func<bool> isAtEnd;
    private readonly Func<Token?> peek;
    private readonly Func<int, Token?> peekAt;
    private readonly Action advance;
    private readonly Func<Expression> parseExpressionEscape;
    private readonly Func<Token, bool> isTerminator;

    public CommandShellWordParser(
        Func<Token?> current,
        Func<bool> isAtEnd,
        Func<Token?> peek,
        Func<int, Token?> peekAt,
        Action advance,
        Func<Expression> parseExpressionEscape,
        Func<Token, bool>? isTerminator = null)
    {
        this.current = current;
        this.isAtEnd = isAtEnd;
        this.peek = peek;
        this.peekAt = peekAt;
        this.advance = advance;
        this.parseExpressionEscape = parseExpressionEscape;
        this.isTerminator = isTerminator ?? (_ => false);
    }

    public bool IsCommandOptionStart()
    {
        var minus = this.current();
        if (minus == null || minus.Type != TokenType.Minus)
        {
            return false;
        }

        var next = this.peek();
        if (next == null || next.Start != minus.End || this.isTerminator(next))
        {
            return false;
        }

        if (next.Type == TokenType.Identifier)
        {
            return true;
        }

        if (next.Type == TokenType.Minus)
        {
            var afterDoubleDash = this.peekAt(1);
            return afterDoubleDash != null &&
                   afterDoubleDash.Type == TokenType.Identifier &&
                   afterDoubleDash.Start == next.End &&
                   !this.isTerminator(afterDoubleDash);
        }

        return false;
    }

    public Expression? ParseShellWord()
    {
        var firstToken = this.current();
        if (firstToken == null || this.isAtEnd() || this.isTerminator(firstToken))
        {
            return null;
        }

        if (IsShellWordExpressionStart(firstToken))
        {
            return this.parseExpressionEscape();
        }

        var next = this.peek();
        bool extends = next != null &&
                       next.Start == firstToken.End &&
                       !this.isTerminator(next) &&
                       CanContinueShellWord(next.Type);
        if (!extends && IsPrimaryStandaloneToken(firstToken.Type))
        {
            return this.parseExpressionEscape();
        }

        var wordText = this.ReadRawShellWord(firstToken);
        var wordToken = new Token(TokenType.Identifier, wordText, firstToken.Start, wordText.Length);
        return new ConstantExpression(wordToken, new ShellText(wordText));
    }

    private static bool IsShellWordExpressionStart(Token token)
    {
        switch (token.Type)
        {
            case TokenType.String:
            case TokenType.InterpolatedString:
            case TokenType.OpenParenthesis:
            case TokenType.OpenBracket:
            case TokenType.OpenBrace:
                return true;
            case TokenType.Identifier:
                return token.Value.StartsWith('$');
            default:
                return false;
        }
    }

    private static bool IsPrimaryStandaloneToken(TokenType type)
    {
        switch (type)
        {
            case TokenType.Identifier:
            case TokenType.Number:
            case TokenType.Decimal:
                return true;
            default:
                return false;
        }
    }

    private static bool CanContinueShellWord(TokenType type)
    {
        switch (type)
        {
            case TokenType.Identifier:
            case TokenType.Number:
            case TokenType.Decimal:
            case TokenType.Colon:
            case TokenType.Divide:
            case TokenType.Plus:
            case TokenType.Minus:
            case TokenType.Multiply:
            case TokenType.Mod:
            case TokenType.Pow:
            case TokenType.Assignment:
            case TokenType.Equal:
            case TokenType.Not:
            case TokenType.NotEqual:
            case TokenType.LessThan:
            case TokenType.GreaterThan:
            case TokenType.LessThanOrEqual:
            case TokenType.GreaterThanOrEqual:
            case TokenType.And:
            case TokenType.Or:
            case TokenType.Xor:
            case TokenType.Comma:
                return true;
            default:
                return false;
        }
    }

    private string ReadRawShellWord(Token firstToken)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(firstToken.Value);
        int endPos = firstToken.End;
        this.advance();

        while (!this.isAtEnd())
        {
            var token = this.current();
            if (token == null ||
                token.Start != endPos ||
                this.isTerminator(token) ||
                !CanContinueShellWord(token.Type))
            {
                break;
            }

            sb.Append(token.Value);
            endPos = token.End;
            this.advance();
        }

        return sb.ToString();
    }
}