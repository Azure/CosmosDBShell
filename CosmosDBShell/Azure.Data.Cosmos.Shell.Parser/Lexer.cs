// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Represents the different types of tokens that can be recognized by the lexer.
/// </summary>
public enum TokenType
{
    /// <summary>
    /// An identifier token (variable names, command names, etc.).
    /// </summary>
    Identifier,

    /// <summary>
    /// A comment token (starts with '#').
    /// </summary>
    Comment,

    /// <summary>
    /// A pipe operator token ('|').
    /// </summary>
    Pipe,

    /// <summary>
    /// A numeric literal token (integers).
    /// </summary>
    Number,

    /// <summary>
    /// A numeric literal token (decimals).
    /// </summary>
    Decimal,

    /// <summary>
    /// A string literal token (enclosed in single or double quotes).
    /// </summary>
    String,

    /// <summary>
    /// An interpolated string token (starts with '$' followed by double quotes).
    /// </summary>
    InterpolatedString,

    /// <summary>
    /// An assignment operator token ('=').
    /// </summary>
    Assignment,

    /// <summary>
    /// Output redirection operator token ('&gt;'). Synthesized by the parser from a
    /// <see cref="GreaterThan"/> token appearing after a command's arguments.
    /// </summary>
    RedirectOutput,

    /// <summary>
    /// Appending output redirection operator token ('&gt;&gt;'). Synthesized by the parser from two
    /// adjacent <see cref="GreaterThan"/> tokens appearing after a command's arguments.
    /// </summary>
    RedirectAppendOutput,

    /// <summary>
    /// Error output redirection operator token ('err>').
    /// </summary>
    RedirectError,

    /// <summary>
    /// Error output redirection operator token ('err>>').
    /// </summary>
    RedirectAppendError,

    /// <summary>
    /// Opening parenthesis token ('(').
    /// </summary>
    OpenParenthesis,

    /// <summary>
    /// Closing parenthesis token (')').
    /// </summary>
    CloseParenthesis,

    /// <summary>
    /// Opening bracket token ('[').
    /// </summary>
    OpenBracket,

    /// <summary>
    /// Closing bracket token (']').
    /// </summary>
    CloseBracket,

    /// <summary>
    /// Opening brace token ('{').
    /// </summary>
    OpenBrace,

    /// <summary>
    /// Closing brace token ('}').
    /// </summary>
    CloseBrace,

    /// <summary>
    /// Colon token (':').
    /// </summary>
    Colon,

    /// <summary>
    /// Semicolon token (';').
    /// </summary>
    Semicolon,

    /// <summary>
    /// Comma token (',').
    /// </summary>
    Comma,

    /// <summary>
    /// Addition operator token ('+').
    /// </summary>
    Plus,

    /// <summary>
    /// Subtraction operator token ('-').
    /// </summary>
    Minus,

    /// <summary>
    /// Multiplication operator token ('*').
    /// </summary>
    Multiply,

    /// <summary>
    /// Division operator token ('/').
    /// </summary>
    Divide,

    /// <summary>
    /// Equality comparison operator token ('==').
    /// </summary>
    Equal,

    /// <summary>
    /// Inequality comparison operator token ('!=').
    /// </summary>
    NotEqual,

    /// <summary>
    /// Less than comparison operator token ('&lt;').
    /// </summary>
    LessThan,

    /// <summary>
    /// Greater than comparison operator token ('&gt;').
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Less than or equal comparison operator token ('&lt;=').
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Greater than or equal comparison operator token ('&gt;=').
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Modulo operator token ('%').
    /// </summary>
    Mod,

    /// <summary>
    /// Power/exponentiation operator token ('**').
    /// </summary>
    Pow,

    /// <summary>
    /// Logical AND operator token ('&&').
    /// </summary>
    And,

    /// <summary>
    /// Logical OR operator token ('||').
    /// </summary>
    Or,

    /// <summary>
    /// Logical XOR operator token ('^').
    /// </summary>
    Xor,

    /// <summary>
    /// Logical NOT operator token ('!').
    /// </summary>
    Not,

    /// <summary>
    /// End of line token (newline characters).
    /// </summary>
    Eol,
}

internal class Lexer
{
    private readonly string input;
    private readonly Stack<Token> putBackTokens;
    private int position;
    private Token? lastToken;

    public Lexer(string input)
    {
        this.input = input ?? string.Empty;
        this.position = 0;
        this.putBackTokens = new Stack<Token>();
        this.lastToken = null;
    }

    public List<Token> Comments { get; } = new();

    public ErrorList Errors { get; } = new ErrorList();

    public IEnumerable<Token> Tokenize()
    {
        Token? token;
        while ((token = this.NextToken()) != null)
        {
            yield return token;
        }
    }

    public Token? NextToken()
    {
        // If we have tokens that were put back, return them first
        if (this.putBackTokens.Count > 0)
        {
            this.lastToken = this.putBackTokens.Pop();
            return this.lastToken;
        }

        // Otherwise, read the next token from input
        this.lastToken = this.ReadNextToken();
        return this.lastToken;
    }

    public void PutBackToken(Token? token)
    {
        ArgumentNullException.ThrowIfNull(token);

        this.putBackTokens.Push(token);
    }

    private static bool IsIdentifierStart(char ch)
    {
        return char.IsLetter(ch) || ch == '_' || ch == '-' || ch == '.' || ch == '\\' || ch == '$' || ch == '/';
    }

    private static bool IsIdentifierPart(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.' || ch == '\\' || ch == '$' || ch == '/';
    }

    private static bool IsVariableIdentifierPart(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.' || ch == '\\' || ch == '$' || ch == '[' || ch == ']';
    }

    private Token? ReadNextToken()
    {
        this.SkipWhitespace();

        if (this.position >= this.input.Length)
        {
            return null;
        }

        var startPosition = this.position;
        var ch = this.input[this.position];

        // Check for interpolated string $"..."
        if (ch == '$' && this.position + 1 < this.input.Length && this.input[this.position + 1] == '"')
        {
            return this.ReadInterpolatedString(startPosition);
        }

        // Check for numbers first (before multi-character tokens)
        if (char.IsDigit(ch))
        {
            return this.ReadNumber(startPosition);
        }

        // Check for multi-character tokens
        if (this.TryReadMultiCharacterToken(startPosition, out var multiCharToken))
        {
            return multiCharToken;
        }

        // Single character tokens
        switch (ch)
        {
            case '|':
                this.Advance();
                return new Token(TokenType.Pipe, "|", startPosition, 1);

            case '(':
                this.Advance();
                return new Token(TokenType.OpenParenthesis, "(", startPosition, 1);

            case ')':
                this.Advance();
                return new Token(TokenType.CloseParenthesis, ")", startPosition, 1);

            case '[':
                this.Advance();
                return new Token(TokenType.OpenBracket, "[", startPosition, 1);

            case ']':
                this.Advance();
                return new Token(TokenType.CloseBracket, "]", startPosition, 1);

            case '{':
                this.Advance();
                return new Token(TokenType.OpenBrace, "{", startPosition, 1);

            case '}':
                this.Advance();
                return new Token(TokenType.CloseBrace, "}", startPosition, 1);

            case ':':
                this.Advance();
                return new Token(TokenType.Colon, ":", startPosition, 1);

            case ';':
                this.Advance();
                return new Token(TokenType.Semicolon, ";", startPosition, 1);

            case ',':
                this.Advance();
                return new Token(TokenType.Comma, ",", startPosition, 1);

            case '+':
                this.Advance();
                return new Token(TokenType.Plus, "+", startPosition, 1);

            case '-':
                this.Advance();
                return new Token(TokenType.Minus, "-", startPosition, 1);

            case '/':
                // Check if this might be the start of a partition key identifier (e.g., /partitionKey)
                // If followed by a letter, treat as identifier start
                if (this.position + 1 < this.input.Length &&
                    (char.IsLetter(this.input[this.position + 1]) || this.input[this.position + 1] == '_'))
                {
                    return this.ReadIdentifier(startPosition);
                }

                // Otherwise treat as division operator
                this.Advance();
                return new Token(TokenType.Divide, "/", startPosition, 1);

            case '%':
                this.Advance();
                return new Token(TokenType.Mod, "%", startPosition, 1);

            case '^':
                this.Advance();
                return new Token(TokenType.Xor, "^", startPosition, 1);

            case '!':
                this.Advance();
                return new Token(TokenType.Not, "!", startPosition, 1);

            case '\n':
            case '\r':
                var eolStartPos = this.position;
                this.SkipNewline();
                return new Token(TokenType.Eol, Environment.NewLine, startPosition, this.position - eolStartPos);

            case '#':
                return this.ReadComment(startPosition);

            case '"':
                return this.ReadDoubleQuotedString(startPosition);

            case '\'':
                return this.ReadSingleQuotedString(startPosition);

            default:
                if (IsIdentifierStart(ch))
                {
                    return this.ReadIdentifier(startPosition);
                }
                else
                {
                    // Unknown character, treat as single character identifier
                    this.Advance();
                    return new Token(TokenType.Identifier, ch.ToString(), startPosition, 1);
                }
        }
    }

    private bool TryReadMultiCharacterToken(int startPosition, out Token? token)
    {
        token = null;

        if (this.LookAhead("err>>"))
        {
            this.Advance(5);
            token = new Token(TokenType.RedirectAppendError, "err>>", startPosition, 5);
            return true;
        }

        // Check for "err>"
        if (this.LookAhead("err>"))
        {
            this.Advance(4);
            token = new Token(TokenType.RedirectError, "err>", startPosition, 4);
            return true;
        }

        // Check for "&&" (logical and)
        if (this.LookAhead("&&"))
        {
            this.Advance(2);
            token = new Token(TokenType.And, "&&", startPosition, 2);
            return true;
        }

        // Check for "||" (logical or)
        if (this.LookAhead("||"))
        {
            this.Advance(2);
            token = new Token(TokenType.Or, "||", startPosition, 2);
            return true;
        }

        // Check for "**" (power operator)
        if (this.LookAhead("**"))
        {
            this.Advance(2);
            token = new Token(TokenType.Pow, "**", startPosition, 2);
            return true;
        }

        // Check for "=="
        if (this.LookAhead("=="))
        {
            this.Advance(2);
            token = new Token(TokenType.Equal, "==", startPosition, 2);
            return true;
        }

        // Check for "!="
        if (this.LookAhead("!="))
        {
            this.Advance(2);
            token = new Token(TokenType.NotEqual, "!=", startPosition, 2);
            return true;
        }

        // Check for "<="
        if (this.LookAhead("<="))
        {
            this.Advance(2);
            token = new Token(TokenType.LessThanOrEqual, "<=", startPosition, 2);
            return true;
        }

        // Check for ">="
        if (this.LookAhead(">="))
        {
            this.Advance(2);
            token = new Token(TokenType.GreaterThanOrEqual, ">=", startPosition, 2);
            return true;
        }

        // Single character operators that could be part of multi-character operators
        var ch = this.input[this.position];
        switch (ch)
        {
            case '*':
                this.Advance();
                token = new Token(TokenType.Multiply, "*", startPosition, 1);
                return true;

            case '=':
                this.Advance();
                token = new Token(TokenType.Assignment, "=", startPosition, 1);
                return true;

            case '<':
                this.Advance();
                token = new Token(TokenType.LessThan, "<", startPosition, 1);
                return true;

            case '>':
                this.Advance();
                token = new Token(TokenType.GreaterThan, ">", startPosition, 1);
                return true;
        }

        return false;
    }

    private Token ReadComment(int startPosition)
    {
        var sb = new StringBuilder();

        // Include the '#'
        sb.Append(this.input[this.position]);
        this.Advance();

        // Read until end of line
        while (this.position < this.input.Length && this.input[this.position] != '\n' && this.input[this.position] != '\r')
        {
            sb.Append(this.input[this.position]);
            this.Advance();
        }

        var commentToken = new Token(TokenType.Comment, sb.ToString(), startPosition, this.position - startPosition);
        this.Comments.Add(commentToken);
        return commentToken;
    }

    private Token ReadIdentifier(int startPosition)
    {
        var sb = new StringBuilder();

        var startChar = this.input[this.position];

        if (startChar == '$')
        {
            int bracketDepth = 0;
            while (this.position < this.input.Length && IsVariableIdentifierPart(this.input[this.position]))
            {
                if (this.input[this.position] == '[')
                {
                    bracketDepth++;
                }
                else if (this.input[this.position] == ']')
                {
                    bracketDepth--;
                    if (bracketDepth < 0)
                    {
                        // Unmatched closing bracket, treat as identifier
                        break;
                    }
                }

                sb.Append(this.input[this.position]);
                this.Advance();
            }
        }
        else
        {
            while (this.position < this.input.Length && IsIdentifierPart(this.input[this.position]))
            {
                sb.Append(this.input[this.position]);
                this.Advance();
            }
        }

        return new Token(TokenType.Identifier, sb.ToString(), startPosition, this.position - startPosition);
    }

    private Token ReadDoubleQuotedString(int startPosition)
    {
        var sb = new StringBuilder();
        bool hasInterpolation = false;

        // Skip opening quote
        this.Advance();

        while (this.position < this.input.Length)
        {
            var ch = this.input[this.position];

            if (ch == '"')
            {
                // Skip closing quote
                this.Advance();
                break;
            }
            else if (ch == '\\' && this.position + 1 < this.input.Length)
            {
                // Handle escape sequences
                this.Advance();
                ch = this.input[this.position];
                switch (ch)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    default: sb.Append(ch); break;
                }

                this.Advance();
            }
            else
            {
                // Track whether the string contains an unescaped '$' so we can treat it as
                // an interpolated string at parse-time ("... $var ...").
                if (ch == '$')
                {
                    hasInterpolation = true;
                }

                sb.Append(ch);
                this.Advance();
            }
        }

        var tokenType = hasInterpolation ? TokenType.InterpolatedString : TokenType.String;
        return new Token(tokenType, sb.ToString(), startPosition, this.position - startPosition);
    }

    private Token ReadSingleQuotedString(int startPosition)
    {
        var sb = new StringBuilder();

        // Skip opening quote
        this.Advance();

        while (this.position < this.input.Length)
        {
            var ch = this.input[this.position];

            if (ch == '\'')
            {
                // Check if it's an escaped quote (two single quotes)
                if (this.position + 1 < this.input.Length && this.input[this.position + 1] == '\'')
                {
                    // It's an escaped quote, add one quote to the result
                    sb.Append('\'');
                    this.Advance(); // skip first quote
                    this.Advance(); // skip second quote
                }
                else
                {
                    // It's the closing quote
                    this.Advance();
                    break;
                }
            }
            else
            {
                // Regular character
                sb.Append(ch);
                this.Advance();
            }
        }

        return new Token(TokenType.String, sb.ToString(), startPosition, this.position - startPosition);
    }

    private Token ReadNumber(int startPosition)
    {
        var sb = new StringBuilder();
        bool hasDecimalPoint = false;
        bool hasExponent = false;

        // Read all digits
        while (this.position < this.input.Length && char.IsDigit(this.input[this.position]))
        {
            sb.Append(this.input[this.position]);
            this.Advance();
        }

        // Check for decimal point (optional)
        if (this.position < this.input.Length && this.input[this.position] == '.')
        {
            // Peek ahead to see if there's a digit after the decimal
            if (this.position + 1 < this.input.Length && char.IsDigit(this.input[this.position + 1]))
            {
                hasDecimalPoint = true;
                sb.Append('.');
                this.Advance();

                // Read decimal digits
                while (this.position < this.input.Length && char.IsDigit(this.input[this.position]))
                {
                    sb.Append(this.input[this.position]);
                    this.Advance();
                }
            }

            // If no digit follows the dot, it's not part of the number
        }

        // Check for exponent (e or E)
        if (this.position < this.input.Length &&
            (this.input[this.position] == 'e' || this.input[this.position] == 'E'))
        {
            var expStart = this.position;
            sb.Append(this.input[this.position]);
            this.Advance();

            // Check for optional sign
            if (this.position < this.input.Length &&
                (this.input[this.position] == '+' || this.input[this.position] == '-'))
            {
                sb.Append(this.input[this.position]);
                this.Advance();
            }

            // Must have at least one digit after e/E (and optional sign)
            if (this.position < this.input.Length && char.IsDigit(this.input[this.position]))
            {
                hasExponent = true;
                while (this.position < this.input.Length && char.IsDigit(this.input[this.position]))
                {
                    sb.Append(this.input[this.position]);
                    this.Advance();
                }
            }
            else
            {
                // Invalid exponent format, backtrack
                this.position = expStart;
                sb.Length -= this.position - expStart;
            }
        }

        // Determine token type based on what we found
        var tokenType = (hasDecimalPoint || hasExponent) ? TokenType.Decimal : TokenType.Number;

        return new Token(tokenType, sb.ToString(), startPosition, this.position - startPosition);
    }

    private void SkipWhitespace()
    {
        while (this.position < this.input.Length)
        {
            var ch = this.input[this.position];
            if (ch == ' ' || ch == '\t')
            {
                this.Advance();
            }
            else
            {
                break;
            }
        }
    }

    private void SkipNewline()
    {
        if (this.position < this.input.Length && this.input[this.position] == '\r')
        {
            this.Advance();
        }

        if (this.position < this.input.Length && this.input[this.position] == '\n')
        {
            this.Advance();
        }
    }

    private void Advance(int count = 1)
    {
        this.position = Math.Min(this.position + count, this.input.Length);
    }

    private bool LookAhead(string text)
    {
        if (this.position + text.Length > this.input.Length)
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (this.input[this.position + i] != text[i])
            {
                return false;
            }
        }

        return true;
    }

    private Token ReadInterpolatedString(int startPosition)
    {
        var sb = new StringBuilder();

        // Skip the '$' and opening quote
        this.Advance(); // skip $
        this.Advance(); // skip "

        while (this.position < this.input.Length)
        {
            var ch = this.input[this.position];

            if (ch == '"')
            {
                // Skip closing quote
                this.Advance();
                break;
            }
            else if (ch == '\\' && this.position + 1 < this.input.Length)
            {
                // Handle escape sequences
                this.Advance();
                ch = this.input[this.position];
                switch (ch)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '{': sb.Append('{'); break;  // Allow escaping braces
                    case '}': sb.Append('}'); break;
                    default: sb.Append(ch); break;
                }

                this.Advance();
            }
            else if (ch == '{' && this.position + 1 < this.input.Length && this.input[this.position + 1] == '{')
            {
                // Handle escaped opening brace {{
                sb.Append('{');
                this.Advance(); // skip first {
                this.Advance(); // skip second {
            }
            else if (ch == '}' && this.position + 1 < this.input.Length && this.input[this.position + 1] == '}')
            {
                // Handle escaped closing brace }}
                sb.Append('}');
                this.Advance(); // skip first }
                this.Advance(); // skip second }
            }
            else
            {
                // Regular character (including interpolation expressions)
                sb.Append(ch);
                this.Advance();
            }
        }

        return new Token(TokenType.InterpolatedString, sb.ToString(), startPosition, this.position - startPosition);
    }
}