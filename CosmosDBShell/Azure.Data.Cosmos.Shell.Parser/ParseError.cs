// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Specifies the severity level of a parse error.
/// </summary>
public enum ErrorLevel
{
    /// <summary>
    /// Indicates a warning level error.
    /// </summary>
    Warning,

    /// <summary>
    /// Indicates an error level error.
    /// </summary>
    Error,
}

/// <summary>
/// Categorizes a parse error so the REPL can distinguish "input is incomplete"
/// (the parser ran off the end while expecting more) from a definitive
/// syntax error. Continuation-prompt logic only treats incomplete-input
/// kinds as a signal to keep reading more lines.
/// </summary>
public enum ParseErrorKind
{
    /// <summary>
    /// An ordinary syntax error. Reporting it does not imply the input
    /// would be accepted if the user typed more characters.
    /// </summary>
    Generic,

    /// <summary>
    /// The parser hit end-of-input while expecting another token
    /// (unclosed brace, missing right-hand side of an operator, etc.).
    /// </summary>
    UnexpectedEnd,

    /// <summary>
    /// The lexer reached end-of-input inside a string literal without
    /// seeing the closing quote.
    /// </summary>
    UnterminatedString,
}

/// <summary>
/// Represents a parsing error with location, message, and severity.
/// </summary>
public class ParseError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ParseError"/> class.
    /// </summary>
    /// <param name="start">The start index of the error.</param>
    /// <param name="length">The length of the error span.</param>
    /// <param name="message">The error message.</param>
    /// <param name="errorLevel">The severity level of the error.</param>
    /// <param name="kind">The category of error.</param>
    public ParseError(int start, int length, string message, ErrorLevel errorLevel = ErrorLevel.Error, ParseErrorKind kind = ParseErrorKind.Generic)
    {
        this.Start = start;
        this.Length = length;
        this.Message = message ?? throw new ArgumentNullException(nameof(message));
        this.ErrorLevel = errorLevel;
        this.Kind = kind;
    }

    /// <summary>
    /// Gets or sets the start index of the error in the input.
    /// </summary>
    public int Start { get; set; }

    /// <summary>
    /// Gets or sets the length of the error span.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Gets or sets the severity level of the error.
    /// </summary>
    public ErrorLevel ErrorLevel { get; set; }

    /// <summary>
    /// Gets or sets the category of the error.
    /// </summary>
    public ParseErrorKind Kind { get; set; }

    /// <summary>
    /// Creates a new <see cref="ParseError"/> instance with <see cref="ErrorLevel.Error"/>.
    /// </summary>
    /// <param name="start">The start index of the error.</param>
    /// <param name="length">The length of the error span.</param>
    /// <param name="message">The error message.</param>
    /// <returns>A new <see cref="ParseError"/> instance.</returns>
    public static ParseError CreateError(int start, int length, string message)
    {
        return new ParseError(start, length, message, ErrorLevel.Error);
    }

    /// <summary>
    /// Creates a new <see cref="ParseError"/> instance with <see cref="ErrorLevel.Warning"/>.
    /// </summary>
    /// <param name="start">The start index of the warning.</param>
    /// <param name="length">The length of the warning span.</param>
    /// <param name="message">The warning message.</param>
    /// <returns>A new <see cref="ParseError"/> instance.</returns>
    public static ParseError CreateWarning(int start, int length, string message)
    {
        return new ParseError(start, length, message, ErrorLevel.Warning);
    }
}
