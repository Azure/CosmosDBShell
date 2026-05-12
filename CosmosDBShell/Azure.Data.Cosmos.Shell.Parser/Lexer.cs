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
    /// Error output redirection operator token ('2&gt;'). Synthesized by the parser in command
    /// context from a <see cref="Number"/> '2' token immediately followed by a
    /// <see cref="GreaterThan"/> token.
    /// </summary>
    RedirectError,

    /// <summary>
    /// Appending error redirection operator token ('2&gt;&gt;'). Synthesized by the parser in
    /// command context from a <see cref="Number"/> '2' token immediately followed by two
    /// adjacent <see cref="GreaterThan"/> tokens.
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
    /// Optional access token ('?').
    /// </summary>
    Question,

    /// <summary>
    /// End of line token (newline characters).
    /// </summary>
    Eol,
}

internal class Lexer
{
    private readonly string input;
    private readonly Stack<Token> putBackTokens;
    private readonly int positionOffset;

    // Use reference equality on the Token key. Token is a record (value equality),
    // so two distinct tokens with identical Type/Value/Start/Length would otherwise
    // collide as map keys. Reference equality guarantees that only the exact token
    // instance produced by ReadInterpolatedString / ReadDoubleQuotedString in this
    // lexer can retrieve its source map.
    private readonly Dictionary<Token, int[]> interpolatedStringSourceMaps = new(ReferenceEqualityComparer.Instance);

    private int position;
    private Token? lastToken;

    public Lexer(string input)
        : this(input, 0)
    {
    }

    /// <summary>
    /// Creates a lexer that reports token positions shifted by <paramref name="positionOffset"/>.
    /// Used when lexing a substring of a larger source buffer (for example, the contents of
    /// a <c>$(...)</c> interpolation inside an interpolated string) so that the produced
    /// tokens carry positions relative to the outer buffer.
    /// </summary>
    public Lexer(string input, int positionOffset)
    {
        this.input = input ?? string.Empty;
        this.position = 0;
        this.positionOffset = positionOffset;
        this.putBackTokens = new Stack<Token>();
        this.lastToken = null;
    }

    public List<Token> Comments { get; } = new();

    public ErrorList Errors { get; } = new ErrorList();

    /// <summary>
    /// Gets the raw input string this lexer is reading. Combined with
    /// <see cref="PositionOffset"/>, callers can recover the original outer-source
    /// substring underlying any token position produced by this lexer.
    /// </summary>
    internal string RawInput => this.input;

    /// <summary>
    /// Gets the position offset added to every produced token's <c>Start</c> value.
    /// </summary>
    internal int PositionOffset => this.positionOffset;

    /// <summary>
    /// Returns the per-character source-position mapping recorded for a previously produced
    /// interpolated string token, or <c>null</c> if the token did not originate from this
    /// lexer or contained no mapping. Each entry is the absolute source position of the
    /// corresponding character in the cooked token value, taking <see cref="positionOffset"/>
    /// into account.
    /// </summary>
    internal IReadOnlyList<int>? GetInterpolatedStringSourceMap(Token token)
    {
        return this.interpolatedStringSourceMaps.TryGetValue(token, out var map) ? map : null;
    }

    private Token MakeToken(TokenType type, string value, int rawStart, int length)
    {
        return new Token(type, value, rawStart + this.positionOffset, length);
    }

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

        // Check for numbers first (before multi-character tokens).
        // Note: the '2>' / '2>>' stderr redirection operators are recognized in the
        // parser (in command context) by pairing a Number("2") token with an
        // adjacent GreaterThan token. Doing that at lex time would break
        // expressions like '2>3' by consuming the '2' as part of a redirect
        // token instead of a numeric literal.
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
                return this.MakeToken(TokenType.Pipe, "|", startPosition, 1);

            case '(':
                this.Advance();
                return this.MakeToken(TokenType.OpenParenthesis, "(", startPosition, 1);

            case ')':
                this.Advance();
                return this.MakeToken(TokenType.CloseParenthesis, ")", startPosition, 1);

            case '[':
                this.Advance();
                return this.MakeToken(TokenType.OpenBracket, "[", startPosition, 1);

            case ']':
                this.Advance();
                return this.MakeToken(TokenType.CloseBracket, "]", startPosition, 1);

            case '{':
                this.Advance();
                return this.MakeToken(TokenType.OpenBrace, "{", startPosition, 1);

            case '}':
                this.Advance();
                return this.MakeToken(TokenType.CloseBrace, "}", startPosition, 1);

            case ':':
                this.Advance();
                return this.MakeToken(TokenType.Colon, ":", startPosition, 1);

            case ';':
                this.Advance();
                return this.MakeToken(TokenType.Semicolon, ";", startPosition, 1);

            case ',':
                this.Advance();
                return this.MakeToken(TokenType.Comma, ",", startPosition, 1);

            case '+':
                this.Advance();
                return this.MakeToken(TokenType.Plus, "+", startPosition, 1);

            case '-':
                this.Advance();
                return this.MakeToken(TokenType.Minus, "-", startPosition, 1);

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
                return this.MakeToken(TokenType.Divide, "/", startPosition, 1);

            case '%':
                this.Advance();
                return this.MakeToken(TokenType.Mod, "%", startPosition, 1);

            case '^':
                this.Advance();
                return this.MakeToken(TokenType.Xor, "^", startPosition, 1);

            case '!':
                this.Advance();
                return this.MakeToken(TokenType.Not, "!", startPosition, 1);

            case '?':
                this.Advance();
                return new Token(TokenType.Question, "?", startPosition, 1);

            case '\n':
            case '\r':
                var eolStartPos = this.position;
                this.SkipNewline();
                return this.MakeToken(TokenType.Eol, Environment.NewLine, startPosition, this.position - eolStartPos);

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
                    return this.MakeToken(TokenType.Identifier, ch.ToString(), startPosition, 1);
                }
        }
    }

    private bool TryReadMultiCharacterToken(int startPosition, out Token? token)
    {
        token = null;

        // Check for "&&" (logical and)
        if (this.LookAhead("&&"))
        {
            this.Advance(2);
            token = this.MakeToken(TokenType.And, "&&", startPosition, 2);
            return true;
        }

        // Check for "||" (logical or)
        if (this.LookAhead("||"))
        {
            this.Advance(2);
            token = this.MakeToken(TokenType.Or, "||", startPosition, 2);
            return true;
        }

        // Check for "**" (power operator)
        if (this.LookAhead("**"))
        {
            this.Advance(2);
            token = this.MakeToken(TokenType.Pow, "**", startPosition, 2);
            return true;
        }

        // Check for "=="
        if (this.LookAhead("=="))
        {
            this.Advance(2);
            token = this.MakeToken(TokenType.Equal, "==", startPosition, 2);
            return true;
        }

        // Check for "!="
        if (this.LookAhead("!="))
        {
            this.Advance(2);
            token = this.MakeToken(TokenType.NotEqual, "!=", startPosition, 2);
            return true;
        }

        // Check for "<="
        if (this.LookAhead("<="))
        {
            this.Advance(2);
            token = this.MakeToken(TokenType.LessThanOrEqual, "<=", startPosition, 2);
            return true;
        }

        // Check for ">="
        if (this.LookAhead(">="))
        {
            this.Advance(2);
            token = this.MakeToken(TokenType.GreaterThanOrEqual, ">=", startPosition, 2);
            return true;
        }

        // Single character operators that could be part of multi-character operators
        var ch = this.input[this.position];
        switch (ch)
        {
            case '*':
                this.Advance();
                token = this.MakeToken(TokenType.Multiply, "*", startPosition, 1);
                return true;

            case '=':
                this.Advance();
                token = this.MakeToken(TokenType.Assignment, "=", startPosition, 1);
                return true;

            case '<':
                this.Advance();
                token = this.MakeToken(TokenType.LessThan, "<", startPosition, 1);
                return true;

            case '>':
                this.Advance();
                token = this.MakeToken(TokenType.GreaterThan, ">", startPosition, 1);
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

        var commentToken = this.MakeToken(TokenType.Comment, sb.ToString(), startPosition, this.position - startPosition);
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

        return this.MakeToken(TokenType.Identifier, sb.ToString(), startPosition, this.position - startPosition);
    }

    private Token ReadDoubleQuotedString(int startPosition)
    {
        var sb = new StringBuilder();
        bool hasInterpolation = false;

        // Mirrors the source-position tracking in ReadInterpolatedString so callers can
        // map cooked content indices back to absolute outer-source positions when the
        // string contains "$..." interpolations.
        var sourcePositions = new List<int>();

        // Skip opening quote
        this.Advance();

        while (this.position < this.input.Length)
        {
            var sourcePos = this.position + this.positionOffset;
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

                sourcePositions.Add(sourcePos);
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
                sourcePositions.Add(sourcePos);
                this.Advance();
            }
        }

        var tokenType = hasInterpolation ? TokenType.InterpolatedString : TokenType.String;
        var token = this.MakeToken(tokenType, sb.ToString(), startPosition, this.position - startPosition);
        if (hasInterpolation)
        {
            this.interpolatedStringSourceMaps[token] = sourcePositions.ToArray();
        }

        return token;
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

        return this.MakeToken(TokenType.String, sb.ToString(), startPosition, this.position - startPosition);
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

        return this.MakeToken(tokenType, sb.ToString(), startPosition, this.position - startPosition);
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

        // Records the absolute outer-source position of the source character that
        // produced each cooked character appended to <c>sb</c>. Used by callers
        // (notably <see cref="ExpressionParser.ParseInterpolatedStringExpression"/>)
        // to map indices in the cooked content back to positions in the original
        // input, which is required for syntax highlighting of nested
        // <c>$(...)</c> interpolations and <c>$VAR</c> references.
        var sourcePositions = new List<int>();

        // Skip the '$' and opening quote
        this.Advance(); // skip $
        this.Advance(); // skip "

        while (this.position < this.input.Length)
        {
            var sourcePos = this.position + this.positionOffset;
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

                sourcePositions.Add(sourcePos);
                this.Advance();
            }
            else if (ch == '{' && this.position + 1 < this.input.Length && this.input[this.position + 1] == '{')
            {
                // Handle escaped opening brace {{
                sb.Append('{');
                sourcePositions.Add(sourcePos);
                this.Advance(); // skip first {
                this.Advance(); // skip second {
            }
            else if (ch == '}' && this.position + 1 < this.input.Length && this.input[this.position + 1] == '}')
            {
                // Handle escaped closing brace }}
                sb.Append('}');
                sourcePositions.Add(sourcePos);
                this.Advance(); // skip first }
                this.Advance(); // skip second }
            }
            else
            {
                // Regular character (including interpolation expressions)
                sb.Append(ch);
                sourcePositions.Add(sourcePos);
                this.Advance();
            }
        }

        var token = this.MakeToken(TokenType.InterpolatedString, sb.ToString(), startPosition, this.position - startPosition);
        this.interpolatedStringSourceMaps[token] = sourcePositions.ToArray();
        return token;
    }
}