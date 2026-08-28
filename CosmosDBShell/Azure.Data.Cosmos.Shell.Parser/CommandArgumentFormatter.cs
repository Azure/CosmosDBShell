// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;

using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Renders a command argument back to shell source text.
/// </summary>
internal static class CommandArgumentFormatter
{
    /// <summary>
    /// Returns the source representation of <paramref name="argument"/>.
    /// </summary>
    public static string Format(Expression argument)
    {
        // Only '=' and ':' values are stored on CommandOption; space-separated values
        // remain separate AST arguments and are bound later using command metadata.
        if (argument is CommandOption option)
        {
            var dashCount = Math.Max(1, option.NameToken.Start - option.MinusToken.Start);
            var text = new string('-', dashCount) + option.Name;
            return option.Value == null ? text : $"{text}{option.SeparatorToken?.Value ?? "="}{Format(option.Value)}";
        }

        // A string constant carries a cooked value, so it has to be re-quoted to stay a
        // literal; every other expression is code and must be emitted verbatim.
        if (argument is ConstantExpression constant && constant.Token.Type == TokenType.String)
        {
            return ShellLiteral.Quote(constant.Token.Value);
        }

        if (argument is InterpolatedStringExpression interpolatedString)
        {
            return interpolatedString.SourceText;
        }

        return argument.ToString() ?? string.Empty;
    }
}
