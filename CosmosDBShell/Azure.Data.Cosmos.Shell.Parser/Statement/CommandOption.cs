// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

internal class CommandOption : Expression
{
    public CommandOption(Token minusToken, Token nameToken, Token? separatorToken = null, Expression? value = null)
    {
        this.MinusToken = minusToken ?? throw new ArgumentNullException(nameof(minusToken));
        this.NameToken = nameToken ?? throw new ArgumentNullException(nameof(nameToken));
        this.SeparatorToken = separatorToken;
        this.Value = value;
    }

    public Token MinusToken { get; }

    public Token NameToken { get; }

    public Token? SeparatorToken { get; }

    public string Name { get => this.NameToken.Value; }

    public Expression? Value { get; set; }

    public override int Start => this.MinusToken.Start;

    public override int Length => (this.Value != null ? this.Value.Start + this.Value.Length : this.NameToken.Start + this.NameToken.Length) - this.MinusToken.Start;

    public override async Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        var text = CommandArgumentFormatter.FormatOptionName(this);
        if (this.Value != null)
        {
            var value = await this.Value.EvaluateAsync(interpreter, currentState, cancellationToken);
            if (value.ConvertShellObject(DataType.Text) is not string valueText)
            {
                throw new InvalidOperationException(MessageService.GetArgsString("statement_error_invalid_option_value", "option", this.Name));
            }

            text += (this.SeparatorToken?.Value ?? "=") + valueText;
        }

        return new ShellText(text);
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}
