// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Represents a lexical token parsed from input, including its type, value, and position information.
/// </summary>
public record Token(TokenType Type, string Value, int Start, int Length)
{
    /// <summary>
    /// Gets the end position of the token (Start + Length).
    /// </summary>
    public int End => this.Start + this.Length;
}