// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

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
        // An option value only binds to its option through '=' or ':', never through a space.
        if (argument is CommandOption option)
        {
            var text = option.MinusToken.Value + option.Name;
            return option.Value == null ? text : $"{text}={Format(option.Value)}";
        }

        // A string constant carries a cooked value, so it has to be re-quoted to stay a
        // literal; every other expression is code and must be emitted verbatim.
        if (argument is ConstantExpression constant && constant.Token.Type == TokenType.String)
        {
            return ShellLiteral.Quote(constant.Token.Value);
        }

        return argument.ToString() ?? string.Empty;
    }
}
