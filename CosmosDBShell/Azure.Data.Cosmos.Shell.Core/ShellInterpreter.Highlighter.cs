// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System.Globalization;
using System.Text;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.AspNetCore.Http.Metadata;
using RadLine;
using Spectre.Console;
using Spectre.Console.Rendering;

/// <summary>
/// Provides syntax highlighting functionality for shell command input by implementing the <see cref="IHighlighter"/> interface.
/// </summary>
/// <remarks>
/// The <c>ShellInterpreter</c> class is responsible for parsing command line input, identifying commands, arguments, options,
/// and special tokens, and applying appropriate formatting using the current theme. It supports highlighting known commands,
/// script paths, unknown commands, database and container names, and output redirection. The highlighting logic is sensitive
/// to the context of each argument and option, and leverages application metadata to determine valid commands and options.
/// </remarks>
public partial class ShellInterpreter : IHighlighter
{
    private string? oldHighlightedText = null;
    private Statement? oldHighlightStatement = null;

    /// <inheritdoc/>
    IRenderable IHighlighter.BuildHighlightedText(string text)
    {
        var parser = new StatementParser(text);
        Statement? statement = null;

#pragma warning disable CZ0001 // Empty Catch Clause
        try
        {
            statement = parser.ParseStatement();
        }
        catch
        {
            // Ignore parse errors for highlighting purposes
        }

        if (statement != null && !parser.Errors.HasErrors)
        {
            try
            {
                var highlighter = new HighlightingVisitor(text, this);
                statement.Accept(highlighter);
                var result = highlighter.GetResult();
                this.oldHighlightStatement = statement;
                return new Markup(result);
            }
            catch
            {
                // Ignore errors
            }
        }
        else
        {
            try
            {
                if (this.oldHighlightStatement != null && this.oldHighlightedText != null && text.StartsWith(this.oldHighlightedText))
                {
                    var highlighter = new HighlightingVisitor(text, this);
                    this.oldHighlightedText = text;
                    this.oldHighlightStatement.Accept(highlighter);
                    var result = highlighter.GetResult();
                    return new Markup(result);
                }
            }
            catch
            {
                // Ignore errors
            }
        }
#pragma warning restore CZ0001 // Empty Catch Clause

        // fall back to non highlighted text in case of any errors.
        return new Markup(Markup.Escape(text));
    }

    private void ClearHighlightStatement()
    {
        this.oldHighlightStatement = null;
    }

    internal class HighlightingVisitor : IAstVisitor
    {
        private readonly string text;
        private readonly ShellInterpreter interpreter;
        private readonly StringBuilder result;
        private int currentPosition;
        private string? currentCommand;

        public HighlightingVisitor(string text, ShellInterpreter interpreter)
        {
            this.text = text;
            this.interpreter = interpreter;
            this.result = new StringBuilder();
            this.currentPosition = 0;
        }

        public string GetResult()
        {
            // Append any remaining text
            if (this.currentPosition < this.text.Length)
            {
                this.result.Append(Markup.Escape(this.text.Substring(this.currentPosition)));
            }

            return this.result.ToString();
        }

        public void VisitToken(Token token)
        {
            // Not used in this context
        }

        public void Visit(CommandOption commandOption)
        {
            // Highlight the option name
            var optionToken = commandOption.NameToken;
            this.AppendUpTo(commandOption.Start);
            string optionMarkup;

            var content = this.text.Substring(commandOption.Start, commandOption.NameToken.Start + commandOption.NameToken.Length - commandOption.Start);

            if (this.interpreter.App.IsOptionPrefix(this.currentCommand, commandOption.Name))
            {
                optionMarkup = Theme.FormatArgumentName(content);
            }
            else
            {
                optionMarkup = Theme.FormatUnknownCommand(content);
            }

            this.result.Append(optionMarkup);

            this.currentPosition = commandOption.NameToken.Start + commandOption.NameToken.Length;

            // Visit the value if it exists
            if (commandOption.Value != null)
            {
                commandOption.Value.Accept(this);
            }
        }

        public void Visit(CommandStatement commandStatement)
        {
            // Highlight the command name
            var cmdToken = commandStatement.CommandToken;
            this.AppendUpTo(cmdToken.Start);

            string cmdMarkup;
            if (commandStatement.Name == "?" || commandStatement.Name == "help" || this.interpreter.App.IsCommand(commandStatement.Name))
            {
                cmdMarkup = Theme.FormatCommand(cmdToken.Value);
            }
            else if (File.Exists(cmdToken.Value))
            {
                cmdMarkup = Theme.FormatScriptPath(cmdToken.Value);
            }
            else
            {
                cmdMarkup = Theme.FormatUnknownCommand(cmdToken.Value);
            }

            this.AppendToken(cmdToken, cmdMarkup);

            this.currentCommand = commandStatement.Name;

            // Visit arguments
            foreach (var arg in commandStatement.Arguments)
            {
                arg.Accept(this);
            }

            if (commandStatement.OutRedirectToken != null && commandStatement.ErrRedirectToken != null)
            {
                // Handle both redirects
                var first = commandStatement.OutRedirectToken.Start < commandStatement.ErrRedirectToken.Start
                    ? (Op: commandStatement.OutRedirectToken, Dest: commandStatement.OutRedirectDestToken)
                    : (Op: commandStatement.ErrRedirectToken, Dest: commandStatement.ErrRedirectDestToken);
                var second = first.Op == commandStatement.OutRedirectToken
                    ? (Op: commandStatement.ErrRedirectToken, Dest: commandStatement.ErrRedirectDestToken)
                    : (Op: commandStatement.OutRedirectToken, Dest: commandStatement.OutRedirectDestToken);

                this.RenderRedirection(first.Op, first.Dest);
                this.RenderRedirection(second.Op, second.Dest);
            }
            else if (commandStatement.OutRedirectToken != null)
            {
                this.RenderRedirection(commandStatement.OutRedirectToken, commandStatement.OutRedirectDestToken);
            }
            else if (commandStatement.ErrRedirectToken != null)
            {
                this.RenderRedirection(commandStatement.ErrRedirectToken, commandStatement.ErrRedirectDestToken);
            }

            this.currentCommand = null;
        }

        private void RenderRedirection(Token opToken, Token? destToken)
        {
            this.AppendUpTo(opToken.Start);
            this.result.Append(Theme.FormatRedirection(opToken.Value));
            this.currentPosition = opToken.End;

            if (destToken != null)
            {
                this.AppendUpTo(destToken.Start);
                var destText = this.text.Substring(destToken.Start, destToken.Length);
                this.result.Append(Theme.FormatRedirectionDestination(destText));
                this.currentPosition = destToken.End;
            }
        }

        public void Visit(ConstantExpression constantExpression)
        {
            // Append the text up to the start of the constant expression
            this.AppendUpTo(constantExpression.Start);

            // Get the text content of the constant expression
            var content = this.text.Substring(constantExpression.Start, constantExpression.Length);

            // For now, just escape and append the content
            // In the future, we can add specific highlighting based on the type of constant
            switch (constantExpression.Value.DataType)
            {
                case DataType.Text:
                    if (constantExpression.Value is ShellText)
                    {
                        this.result.Append(Theme.FormatStringLiteral(content));
                    }
                    else
                    {
                        this.result.Append(Markup.Escape(content));
                    }

                    break;
                case DataType.Decimal:
                case DataType.Number:
                    this.result.Append(Theme.FormatNumberLiteral(content));
                    break;
                case DataType.Boolean:
                    this.result.Append(Theme.FormatBooleanLiteral(content));
                    break;
                default:
                    this.result.Append(Markup.Escape(content));
                    break;
            }

            // Update the current position
            this.currentPosition = constantExpression.Start + constantExpression.Length;
        }

        public void Visit(ErrorExpression errorExpression)
        {
            // Highlight the entire error expression as an error
            this.AppendUpTo(errorExpression.Start);
            var content = this.text.Substring(errorExpression.Start, errorExpression.Length);
            this.result.Append(Theme.FormatError(content));
            this.currentPosition = errorExpression.Start + errorExpression.Length;
        }

        public void Visit(UnaryOperatorExpression unaryOperatorExpression)
        {
            this.AppendUpTo(unaryOperatorExpression.OperatorToken.Start);
            this.result.Append(Theme.FormatOperator(unaryOperatorExpression.OperatorToken.Value));
            this.currentPosition = unaryOperatorExpression.OperatorToken.End;
            unaryOperatorExpression.Expression.Accept(this);
        }

        public void Visit(BinaryOperatorExpression binaryOperatorExpression)
        {
            binaryOperatorExpression.Left.Accept(this);
            this.AppendUpTo(binaryOperatorExpression.OperatorToken.Start);
            this.result.Append(Theme.FormatOperator(binaryOperatorExpression.OperatorToken.Value));
            this.currentPosition = binaryOperatorExpression.OperatorToken.End;
            binaryOperatorExpression.Right.Accept(this);
        }

        public void Visit(ParensExpression parensExpression)
        {
            parensExpression.InnerExpression.Accept(this);
        }

        public void Visit(JsonExpression jsonExpression)
        {
            // Find the opening brace position in the text
            var startPos = jsonExpression.Start;
            this.AppendUpTo(startPos);

            // Find and highlight the opening brace
            var openBracePos = this.text.IndexOf('{', this.currentPosition);
            if (openBracePos >= 0 && openBracePos < jsonExpression.Start + jsonExpression.Length)
            {
                this.AppendUpTo(openBracePos);
                this.result.Append(Theme.FormatJsonBracket("{"));
                this.currentPosition = openBracePos + 1;
            }

            // Process the properties
            foreach (var property in jsonExpression.Properties)
            {
                // Preserve whitespace before property name by using AppendUpTo
                // Find the next non-whitespace character (start of property name)
                var propertyStart = this.currentPosition;

                // Skip whitespace to find where property actually starts
                while (propertyStart < this.text.Length &&
                       propertyStart < jsonExpression.Start + jsonExpression.Length &&
                       char.IsWhiteSpace(this.text[propertyStart]))
                {
                    propertyStart++;
                }

                // Determine the property name format and find its end
                var propertyEnd = propertyStart;
                if (propertyStart < this.text.Length)
                {
                    if (this.text[propertyStart] == '"' || this.text[propertyStart] == '\'')
                    {
                        // Quoted property name
                        var quote = this.text[propertyStart];
                        propertyEnd = this.text.IndexOf(quote, propertyStart + 1);
                        if (propertyEnd >= 0)
                        {
                            propertyEnd++; // Include closing quote
                        }
                    }
                    else
                    {
                        // Unquoted identifier
                        while (propertyEnd < this.text.Length &&
                               (char.IsLetterOrDigit(this.text[propertyEnd]) ||
                                this.text[propertyEnd] == '_' ||
                                this.text[propertyEnd] == '-'))
                        {
                            propertyEnd++;
                        }
                    }
                }

                if (propertyEnd > propertyStart && propertyEnd <= this.text.Length)
                {
                    // Use AppendUpTo to preserve the whitespace before the property
                    this.AppendUpTo(propertyStart);
                    var propertyText = this.text.Substring(propertyStart, propertyEnd - propertyStart);
                    this.result.Append(Theme.FormatJsonProperty(propertyText));
                    this.currentPosition = propertyEnd;
                }

                // Find and highlight the colon
                var colonPos = this.text.IndexOf(':', this.currentPosition);
                if (colonPos >= 0 && colonPos < jsonExpression.Start + jsonExpression.Length)
                {
                    this.AppendUpTo(colonPos);
                    this.result.Append(Theme.FormatJsonBracket(":"));
                    this.currentPosition = colonPos + 1;
                }

                // Visit the value expression
                property.Value.Accept(this);

                // Look for comma
                var commaPos = this.text.IndexOf(',', this.currentPosition);
                var closeBracePos = this.text.IndexOf('}', this.currentPosition);

                // Only highlight comma if it comes before the closing brace
                if (commaPos >= 0 && commaPos < jsonExpression.Start + jsonExpression.Length &&
                    (closeBracePos < 0 || commaPos < closeBracePos))
                {
                    this.AppendUpTo(commaPos);
                    this.result.Append(Theme.FormatJsonBracket(","));
                    this.currentPosition = commaPos + 1;
                }
            }

            // Find and highlight the closing brace
            var endBracePos = this.text.LastIndexOf('}', jsonExpression.Start + jsonExpression.Length - 1);
            if (endBracePos >= 0 && endBracePos >= this.currentPosition)
            {
                this.AppendUpTo(endBracePos);
                this.result.Append(Theme.FormatJsonBracket("}"));
                this.currentPosition = endBracePos + 1;
            }
        }

        public void Visit(JsonArrayExpression jsonArrayExpression)
        {
            // Find the opening bracket position in the text
            var startPos = jsonArrayExpression.Start;
            this.AppendUpTo(startPos);

            // Find and highlight the opening bracket
            var openBracketPos = this.text.IndexOf('[', this.currentPosition);
            if (openBracketPos >= 0 && openBracketPos < jsonArrayExpression.Start + jsonArrayExpression.Length)
            {
                this.AppendUpTo(openBracketPos);
                this.result.Append(Theme.FormatJsonBracket("["));
                this.currentPosition = openBracketPos + 1;
            }

            // Process each element in the array
            for (int i = 0; i < jsonArrayExpression.Expressions.Count; i++)
            {
                // Visit the element expression (AppendUpTo in the element's Accept will preserve whitespace)
                jsonArrayExpression.Expressions[i].Accept(this);

                // Look for comma after element (except for last element)
                if (i < jsonArrayExpression.Expressions.Count - 1)
                {
                    // Find the comma position
                    var commaPos = this.text.IndexOf(',', this.currentPosition);
                    if (commaPos >= 0 && commaPos < jsonArrayExpression.Start + jsonArrayExpression.Length)
                    {
                        // AppendUpTo preserves whitespace between element and comma
                        this.AppendUpTo(commaPos);
                        this.result.Append(Theme.FormatJsonBracket(","));
                        this.currentPosition = commaPos + 1;
                    }
                }
            }

            // Find and highlight the closing bracket
            var closeBracketPos = this.text.IndexOf(']', this.currentPosition);
            if (closeBracketPos >= 0 && closeBracketPos < jsonArrayExpression.Start + jsonArrayExpression.Length)
            {
                // AppendUpTo preserves any whitespace before the closing bracket
                this.AppendUpTo(closeBracketPos);
                this.result.Append(Theme.FormatJsonBracket("]"));
                this.currentPosition = closeBracketPos + 1;
            }

            // Ensure we've moved past the entire expression
            this.AppendUpTo(jsonArrayExpression.Start + jsonArrayExpression.Length);
        }

        public void Visit(JSonPathExpression jSonPathExpression)
        {
            this.AppendUpTo(jSonPathExpression.Start + jSonPathExpression.Length);
        }

        public void Visit(VariableExpression variableExpression)
        {
            this.AppendUpTo(variableExpression.Start + variableExpression.Length);
        }

        public void Visit(CommandExpression commandExpression)
        {
            // Highlight the command name
            var cmdToken = commandExpression.CommandToken;
            this.AppendUpTo(cmdToken.Start);

            string cmdMarkup;
            if (commandExpression.Name == "?" || commandExpression.Name == "help" || this.interpreter.App.IsCommand(commandExpression.Name))
            {
                cmdMarkup = Theme.FormatCommand(cmdToken.Value);
            }
            else if (File.Exists(cmdToken.Value))
            {
                cmdMarkup = Theme.FormatScriptPath(cmdToken.Value);
            }
            else
            {
                cmdMarkup = Theme.FormatUnknownCommand(cmdToken.Value);
            }

            this.AppendToken(cmdToken, cmdMarkup);

            var previousCommand = this.currentCommand;
            this.currentCommand = commandExpression.Name;

            // Visit arguments
            foreach (var arg in commandExpression.Arguments)
            {
                arg.Accept(this);
            }

            this.currentCommand = previousCommand;
        }

        public void Visit(InterpolatedStringExpression interpolatedStringExpression)
        {
            foreach (var expr in interpolatedStringExpression.Expressions)
            {
                expr.Accept(this);
            }

            this.AppendUpTo(interpolatedStringExpression.Start + interpolatedStringExpression.Length);
        }

        // Statement visitors
        public void Visit(AssignmentStatement assignmentStatement)
        {
            assignmentStatement.Variable.Accept(this);
            this.AppendUpTo(assignmentStatement.AssignmentToken.Start);
            this.result.Append(Theme.FormatOperator(assignmentStatement.AssignmentToken.Value));
            this.currentPosition = assignmentStatement.AssignmentToken.End;
            assignmentStatement.Value.Accept(this);
        }

        public void Visit(BlockStatement blockStatement)
        {
            foreach (var statement in blockStatement.Statements)
            {
                statement.Accept(this);
            }
        }

        public void Visit(BreakStatement breakStatement)
        {
            this.AppendUpTo(breakStatement.BreakToken.Start);
            this.result.Append(Theme.FormatKeyword(breakStatement.BreakToken.Value));
            this.currentPosition = breakStatement.BreakToken.End;
        }

        public void Visit(ContinueStatement continueStatement)
        {
            this.AppendUpTo(continueStatement.ContinueToken.Start);
            this.result.Append(Theme.FormatKeyword(continueStatement.ContinueToken.Value));
            this.currentPosition = continueStatement.ContinueToken.End;
        }

        public void Visit(DefStatement defStatement)
        {
            this.AppendUpTo(defStatement.DefToken.Start);
            this.result.Append(Theme.FormatKeyword(defStatement.DefToken.Value));
            this.currentPosition = defStatement.DefToken.End;

            defStatement.Statement.Accept(this);
        }

        public void Visit(DoWhileStatement doWhileStatement)
        {
            this.AppendUpTo(doWhileStatement.DoToken.Start);
            this.result.Append(Theme.FormatKeyword(doWhileStatement.DoToken.Value));
            this.currentPosition = doWhileStatement.DoToken.End;
            doWhileStatement.Statement.Accept(this);
            this.AppendUpTo(doWhileStatement.WhileToken.Start);
            this.result.Append(Theme.FormatKeyword(doWhileStatement.WhileToken.Value));
            this.currentPosition = doWhileStatement.WhileToken.End;
            doWhileStatement.Condition.Accept(this);
        }

        public void Visit(ExecStatement execStatement)
        {
            this.AppendUpTo(execStatement.ExecToken.Start);
            this.result.Append(Theme.FormatKeyword(execStatement.ExecToken.Value));
            this.currentPosition = execStatement.ExecToken.End;
            execStatement.CommandExpression.Accept(this);
            foreach (var arg in execStatement.Arguments)
            {
                arg.Accept(this);
            }
        }

        public void Visit(ForStatement forStatement)
        {
            this.AppendUpTo(forStatement.ForToken.Start);
            this.result.Append(Theme.FormatKeyword(forStatement.ForToken.Value));
            this.currentPosition = forStatement.ForToken.End;

            this.AppendUpTo(forStatement.InToken.Start);
            this.result.Append(Theme.FormatKeyword(forStatement.InToken.Value));
            this.currentPosition = forStatement.InToken.End;

            forStatement.Collection.Accept(this);
            forStatement.Statement.Accept(this);
        }

        public void Visit(IfStatement ifStatement)
        {
            this.AppendUpTo(ifStatement.IfToken.Start);
            this.result.Append(Theme.FormatKeyword(ifStatement.IfToken.Value));
            this.currentPosition = ifStatement.IfToken.End;
            ifStatement.Condition.Accept(this);
            ifStatement.Statement.Accept(this);

            if (ifStatement.ElseToken != null)
            {
                this.AppendUpTo(ifStatement.ElseToken.Start);
                this.result.Append(Theme.FormatKeyword(ifStatement.ElseToken.Value));
                this.currentPosition = ifStatement.ElseToken.End;

                ifStatement.ElseStatement?.Accept(this);
            }
        }

        public void Visit(LoopStatement loopStatement)
        {
            this.AppendUpTo(loopStatement.LoopToken.Start);
            this.result.Append(Theme.FormatKeyword(loopStatement.LoopToken.Value));
            this.currentPosition = loopStatement.LoopToken.End;
            loopStatement.Statement.Accept(this);
        }

        public void Visit(PipeStatement pipeStatement)
        {
            foreach (var statement in pipeStatement.Statements)
            {
                statement.Accept(this);
            }
        }

        public void Visit(ReturnStatement returnStatement)
        {
            this.AppendUpTo(returnStatement.ReturnToken.Start);
            this.result.Append(Theme.FormatKeyword(returnStatement.ReturnToken.Value));
            this.currentPosition = returnStatement.ReturnToken.End;

            returnStatement.Value?.Accept(this);
        }

        public void Visit(WhileStatement whileStatement)
        {
            this.AppendUpTo(whileStatement.WhileToken.Start);
            this.result.Append(Theme.FormatKeyword(whileStatement.WhileToken.Value));
            this.currentPosition = whileStatement.WhileToken.End;
            whileStatement.Condition.Accept(this);
            whileStatement.Statement.Accept(this);
        }

        private void AppendUpTo(int position)
        {
            if (position > this.currentPosition && position <= this.text.Length)
            {
                this.result.Append(Markup.Escape(this.text.Substring(this.currentPosition, position - this.currentPosition)));
                this.currentPosition = position;
            }
        }

        private void AppendToken(Token token, string markup)
        {
            this.AppendUpTo(token.Start);
            this.result.Append(markup);
            this.currentPosition = token.Start + token.Length;
        }
    }
}