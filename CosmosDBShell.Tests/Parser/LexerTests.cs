// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Parser;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace CosmosShell.Tests.Parser;

public class LexerTests
{
    [Fact]
    public void NextToken_EmptyInput_ReturnsNull()
    {
        // Arrange
        var lexer = new Lexer("");

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public void NextToken_SingleIdentifier_ReturnsIdentifierToken()
    {
        // Arrange
        var lexer = new Lexer("hello");

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("hello", token.Value);
        Assert.Equal(0, token.Start);
    }

    [Fact]
    public void NextToken_MultipleIdentifiers_ReturnsCorrectTokens()
    {
        // Arrange
        var lexer = new Lexer("hello world");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();
        var token3 = lexer.NextToken();

        // Assert
        Assert.Equal(TokenType.Identifier, token1?.Type);
        Assert.Equal("hello", token1?.Value);
        Assert.Equal(TokenType.Identifier, token2?.Type);
        Assert.Equal("world", token2?.Value);
        Assert.Null(token3);
    }

    [Theory]
    [InlineData("'hello world'", "hello world")]
    [InlineData("'hello''world'", "hello'world")]
    [InlineData("''", "")]
    public void NextToken_SingleQuotedString_ReturnsStringToken(string input, string expected)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.String, token.Type);
        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("\"hello world\"", "hello world")]
    [InlineData("\"hello\\nworld\"", "hello\nworld")]
    [InlineData("\"hello\\tworld\"", "hello\tworld")]
    [InlineData("\"hello\\rworld\"", "hello\rworld")]
    [InlineData("\"hello\\\\world\"", "hello\\world")]
    [InlineData("\"hello\\\"world\"", "hello\"world")]
    [InlineData("\"\"", "")]
    public void NextToken_DoubleQuotedString_ReturnsStringTokenWithEscapes(string input, string expected)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.String, token.Type);
        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("|", TokenType.Pipe, "|")]
    [InlineData("(", TokenType.OpenParenthesis, "(")]
    [InlineData(")", TokenType.CloseParenthesis, ")")]
    [InlineData("[", TokenType.OpenBracket, "[")]
    [InlineData("]", TokenType.CloseBracket, "]")]
    [InlineData("{", TokenType.OpenBrace, "{")]
    [InlineData("}", TokenType.CloseBrace, "}")]
    [InlineData(":", TokenType.Colon, ":")]
    [InlineData(";", TokenType.Semicolon, ";")]
    [InlineData("+", TokenType.Plus, "+")]
    [InlineData("-", TokenType.Minus, "-")]
    [InlineData("/", TokenType.Divide, "/")]
    [InlineData("%", TokenType.Mod, "%")]
    [InlineData("^", TokenType.Xor, "^")]
    [InlineData("!", TokenType.Not, "!")]
    [InlineData("*", TokenType.Multiply, "*")]
    [InlineData("=", TokenType.Assignment, "=")]
    [InlineData("<", TokenType.LessThan, "<")]
    [InlineData(">", TokenType.GreaterThan, ">")]
    public void NextToken_SingleCharacterTokens_ReturnsCorrectToken(string input, TokenType expectedType, string expectedValue)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(expectedType, token.Type);
        Assert.Equal(expectedValue, token.Value);
    }

    [Theory]
    [InlineData("2>", TokenType.RedirectError, "2>")]
    [InlineData("&&", TokenType.And, "&&")]
    [InlineData("||", TokenType.Or, "||")]
    [InlineData("**", TokenType.Pow, "**")]
    [InlineData("==", TokenType.Equal, "==")]
    [InlineData("!=", TokenType.NotEqual, "!=")]
    [InlineData("<=", TokenType.LessThanOrEqual, "<=")]
    [InlineData(">=", TokenType.GreaterThanOrEqual, ">=")]
    public void NextToken_MultiCharacterTokens_ReturnsCorrectToken(string input, TokenType expectedType, string expectedValue)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(expectedType, token.Type);
        Assert.Equal(expectedValue, token.Value);
    }

    [Fact]
    public void NextToken_Comment_ReturnsCommentToken()
    {
        // Arrange
        var lexer = new Lexer("# This is a comment");

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Comment, token.Type);
        Assert.Equal("# This is a comment", token.Value);
    }

    [Fact]
    public void NextToken_CommentFollowedByNewline_StopsAtNewline()
    {
        // Arrange
        var lexer = new Lexer("# comment\nidentifier");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();
        var token3 = lexer.NextToken();

        // Assert
        Assert.Equal(TokenType.Comment, token1?.Type);
        Assert.Equal("# comment", token1?.Value);
        Assert.Equal(TokenType.Eol, token2?.Type);
        Assert.Equal(TokenType.Identifier, token3?.Type);
        Assert.Equal("identifier", token3?.Value);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void NextToken_NewlineCharacters_ReturnsEolToken(string input)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Eol, token.Type);
    }

    [Fact]
    public void NextToken_IdentifierWithSpecialChars_ParsesCorrectly()
    {
        // Arrange
        var lexer = new Lexer("test-file.txt");

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("test-file.txt", token.Value);
    }

    [Fact]
    public void NextToken_VariableIdentifier_ParsesCorrectly()
    {
        // Arrange
        var lexer = new Lexer("$variable");

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("$variable", token.Value);
    }

    [Fact]
    public void NextToken_PathIdentifier_ParsesCorrectly()
    {
        // Arrange
        var lexer = new Lexer("\\Users\\test");

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("\\Users\\test", token.Value);
    }

    [Fact]
    public void PutBackToken_SingleToken_CanBeRetrieved()
    {
        // Arrange
        var lexer = new Lexer("hello world");
        lexer.NextToken();
        var token2 = lexer.NextToken();

        // Act
        Assert.NotNull(token2);
        lexer.PutBackToken(token2);
        var retrievedToken = lexer.NextToken();

        // Assert
        Assert.Equal(token2.Type, retrievedToken?.Type);
        Assert.Equal(token2.Value, retrievedToken?.Value);
        Assert.Equal(token2.Start, retrievedToken?.Start);
    }

    [Fact]
    public void PutBackToken_MultipleTokens_RetrievedInLIFOOrder()
    {
        // Arrange
        var lexer = new Lexer("a b c");
        lexer.NextToken();
        var token2 = lexer.NextToken();
        var token3 = lexer.NextToken();

        // Act
        lexer.PutBackToken(token3);
        lexer.PutBackToken(token2);

        var retrieved1 = lexer.NextToken();
        var retrieved2 = lexer.NextToken();

        // Assert
        Assert.Equal(token2?.Value, retrieved1?.Value); // LIFO
        Assert.Equal(token3?.Value, retrieved2?.Value);
    }

    [Fact]
    public void PutBackToken_NullToken_ThrowsArgumentNullException()
    {
        // Arrange
        var lexer = new Lexer("test");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => lexer.PutBackToken(null));
    }

    [Fact]
    public void NextToken_ComplexExpression_TokenizesCorrectly2()
    {
        // Arrange
        var lexer = new Lexer("$x = 2 + 3 * 4");
        var expectedTokens = new[]
        {
            (TokenType.Identifier, "$x"),
            (TokenType.Assignment, "="),
            (TokenType.Number, "2"),
            (TokenType.Plus, "+"),
            (TokenType.Number, "3"),
            (TokenType.Multiply, "*"),
            (TokenType.Number, "4")
        };

        // Act & Assert
        foreach (var (expectedType, expectedValue) in expectedTokens)
        {
            var token = lexer.NextToken();
            Assert.NotNull(token);
            Assert.Equal(expectedType, token.Type);
            Assert.Equal(expectedValue, token.Value);
        }
    }

    [Fact]
    public void NextToken_BooleanExpression_TokenizesCorrectly()
    {
        // Arrange
        var lexer = new Lexer("true && false || !x");
        var expectedTokens = new[]
        {
            (TokenType.Identifier, "true"),
            (TokenType.And, "&&"),
            (TokenType.Identifier, "false"),
            (TokenType.Or, "||"),
            (TokenType.Not, "!"),
            (TokenType.Identifier, "x")
        };

        // Act & Assert
        foreach (var (expectedType, expectedValue) in expectedTokens)
        {
            var token = lexer.NextToken();
            Assert.NotNull(token);
            Assert.Equal(expectedType, token.Type);
            Assert.Equal(expectedValue, token.Value);
        }
    }

    [Fact]
    public void NextToken_RedirectionOperators_ParseCorrectly()
    {
        // Arrange
        var lexer = new Lexer("command > file.txt 2> error.log");
        var expectedTokens = new[]
        {
            (TokenType.Identifier, "command"),
            (TokenType.GreaterThan, ">"),
            (TokenType.Identifier, "file.txt"),
            (TokenType.RedirectError, "2>"),
            (TokenType.Identifier, "error.log")
        };

        // Act & Assert
        foreach (var (expectedType, expectedValue) in expectedTokens)
        {
            var token = lexer.NextToken();
            Assert.NotNull(token);
            Assert.Equal(expectedType, token.Type);
            Assert.Equal(expectedValue, token.Value);
        }
    }

    [Fact]
    public void NextToken_MixedWhitespace_SkipsCorrectly()
    {
        // Arrange
        var lexer = new Lexer("  \t  hello  \t  world  ");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();
        var token3 = lexer.NextToken();

        // Assert
        Assert.Equal("hello", token1?.Value);
        Assert.Equal("world", token2?.Value);
        Assert.Null(token3);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("0", "0")]
    [InlineData("123456789", "123456789")]
    public void NextToken_Numbers_ReturnsNumberToken(string input, string expected)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Number, token.Type);
        Assert.Equal(expected, token.Value);
    }

    [Fact]
    public void NextToken_NumberFollowedByIdentifier_ParsesSeparately()
    {
        // Arrange
        var lexer = new Lexer("42abc");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();

        // Assert
        Assert.Equal(TokenType.Number, token1?.Type);
        Assert.Equal("42", token1?.Value);
        Assert.Equal(TokenType.Identifier, token2?.Type);
        Assert.Equal("abc", token2?.Value);
    }

    [Fact]
    public void NextToken_DecimalPointWithoutFollowingDigit_ParsesAsSeparateTokens()
    {
        // Arrange
        var lexer = new Lexer("42.");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();

        // Assert
        Assert.Equal(TokenType.Number, token1?.Type);
        Assert.Equal("42", token1?.Value);
        Assert.Equal(TokenType.Identifier, token2?.Type);
        Assert.Equal(".", token2?.Value);
    }

    [Fact]
    public void NextToken_ComplexExpression_TokenizesCorrectly()
    {
        // Arrange
        var lexer = new Lexer("$x = 2 + 3 * 4");
        var expectedTokens = new[]
        {
            (TokenType.Identifier, "$x"),
            (TokenType.Assignment, "="),
            (TokenType.Number, "2"),
            (TokenType.Plus, "+"),
            (TokenType.Number, "3"),
            (TokenType.Multiply, "*"),
            (TokenType.Number, "4")
        };

        // Act & Assert
        foreach (var (expectedType, expectedValue) in expectedTokens)
        {
            var token = lexer.NextToken();
            Assert.NotNull(token);
            Assert.Equal(expectedType, token?.Type);
            Assert.Equal(expectedValue, token?.Value);
        }
    }

    [Fact]
    public void NextToken_NumbersInExpression_ParsesCorrectly()
    {
        // Arrange
        var lexer = new Lexer("3.14 * 2 + 42");
        var expectedTokens = new[]
        {
            (TokenType.Decimal, "3.14"),
            (TokenType.Multiply, "*"),
            (TokenType.Number, "2"),
            (TokenType.Plus, "+"),
            (TokenType.Number, "42")
        };

        // Act & Assert
        foreach (var (expectedType, expectedValue) in expectedTokens)
        {
            var token = lexer.NextToken();
            Assert.NotNull(token);
            Assert.Equal(expectedType, token.Type);
            Assert.Equal(expectedValue, token.Value);
        }
    }

    [Fact]
    public void NextToken_NegativeNumber_ParsesAsMinusAndNumber()
    {
        // Arrange
        var lexer = new Lexer("-42");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();

        // Assert
        Assert.Equal(TokenType.Minus, token1?.Type);
        Assert.Equal("-", token1?.Value);
        Assert.Equal(TokenType.Number, token2?.Type);
        Assert.Equal("42", token2?.Value);
    }

    [Theory]
    [InlineData("$.[0]", "$.[0]")]
    [InlineData("$.[1]", "$.[1]")]
    [InlineData("$.[42]", "$.[42]")]
    [InlineData("$.name", "$.name")]
    [InlineData("$.firstName", "$.firstName")]
    [InlineData("$.address.city", "$.address.city")]
    [InlineData("$.users[0].name", "$.users[0].name")]
    public void NextToken_JsonPathVariables_ParsesAsIdentifier(string input, string expected)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal(expected, token.Value);
    }

    [Fact]
    public void NextToken_JsonPathWithSpaces_ParsesMultipleTokens()
    {
        // Arrange
        var lexer = new Lexer("$. [0]");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();
        var token3 = lexer.NextToken();
        var token4 = lexer.NextToken();

        // Assert
        Assert.Equal(TokenType.Identifier, token1?.Type);
        Assert.Equal("$.", token1?.Value);
        Assert.Equal(TokenType.OpenBracket, token2?.Type);
        Assert.Equal(TokenType.Number, token3?.Type);
        Assert.Equal("0", token3?.Value);
        Assert.Equal(TokenType.CloseBracket, token4?.Type);
    }

    [Fact]
    public void NextToken_ComplexJsonPath_ParsesAsIdentifier()
    {
        // Arrange
        var lexer = new Lexer("$.store.books[0].author");

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("$.store.books[0].author", token.Value);
    }

    [Fact]
    public void NextToken_JsonPathInExpression_TokenizesCorrectly()
    {
        // Arrange
        var lexer = new Lexer("result = $.data[0] + $.data[1]");
        var expectedTokens = new[]
        {
            (TokenType.Identifier, "result"),
            (TokenType.Assignment, "="),
            (TokenType.Identifier, "$.data[0]"),
            (TokenType.Plus, "+"),
            (TokenType.Identifier, "$.data[1]")
        };

        // Act & Assert
        foreach (var (expectedType, expectedValue) in expectedTokens)
        {
            var token = lexer.NextToken();
            Assert.NotNull(token);
            Assert.Equal(expectedType, token.Type);
            Assert.Equal(expectedValue, token.Value);
        }
    }

    [Fact]
    public void NextToken_JsonPathWithNestedBrackets_ParsesAsIdentifier()
    {
        // Arrange
        var lexer = new Lexer("$.users[$.currentIndex]");

        // Act
        var token = lexer.NextToken();

        // Assert
        Assert.NotNull(token);
        Assert.Equal(TokenType.Identifier, token.Type);
        Assert.Equal("$.users[$.currentIndex]", token.Value);
    }

    [Fact]
    public void NextToken_MultipleJsonPathVariables_ParsesCorrectly()
    {
        // Arrange
        var lexer = new Lexer("$.firstName $.lastName $.age");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();
        var token3 = lexer.NextToken();

        // Assert
        Assert.Equal(TokenType.Identifier, token1?.Type);
        Assert.Equal("$.firstName", token1?.Value);
        Assert.Equal(TokenType.Identifier, token2?.Type);
        Assert.Equal("$.lastName", token2?.Value);
        Assert.Equal(TokenType.Identifier, token3?.Type);
        Assert.Equal("$.age", token3?.Value);
    }


    [Fact]
    public void LexSingleVariableArray()
    {
        var lexer = new Lexer("[$i]");

        // Act
        var token1 = lexer.NextToken();
        var token2 = lexer.NextToken();
        var token3 = lexer.NextToken();

        // Assert
        Assert.Equal(TokenType.OpenBracket, token1?.Type);
        Assert.Equal("[", token1?.Value);
        Assert.Equal(TokenType.Identifier, token2?.Type);
        Assert.Equal("$i", token2?.Value);
        Assert.Equal(TokenType.CloseBracket, token3?.Type);
        Assert.Equal("]", token3?.Value);
    }
}