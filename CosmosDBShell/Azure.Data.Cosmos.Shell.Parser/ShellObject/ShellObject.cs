// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

internal abstract class ShellObject
{
    protected ShellObject(DataType type)
    {
        this.DataType = type;
    }

    public DataType DataType { get; }

    public static ShellObject Parse(Lexer lexer)
    {
        var tokens = lexer.Tokenize().GetEnumerator();

        if (!tokens.MoveNext())
        {
            throw new InvalidOperationException("No tokens to parse");
        }

        var token = tokens.Current;

        switch (token.Type)
        {
            case TokenType.Identifier:
                // Check if it's a boolean literal
                if (string.Equals(token.Value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return new ShellBool(true);
                }

                if (string.Equals(token.Value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return new ShellBool(false);
                }

                // Check if it's a number
                if (int.TryParse(token.Value, out int intValue))
                {
                    return new ShellNumber(intValue);
                }

                // Otherwise, treat it as text
                return new ShellIdentifier(token.Value);

            case TokenType.String:
                // Quoted strings are always text
                return new ShellText(token.Value);
            default:
                // For any other token type, treat as text
                return new ShellIdentifier(token.Value);
        }
    }

    public abstract object? ConvertShellObject(DataType type);
}
