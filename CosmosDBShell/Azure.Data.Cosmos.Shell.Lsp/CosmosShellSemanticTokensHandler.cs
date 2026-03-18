// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Parser;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal class CosmosShellSemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly CosmosShellWorkspace workspace;

    public CosmosShellSemanticTokensHandler(
        CosmosShellWorkspace workspace)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = LspServer.DocumentSelector,
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(
                    SemanticTokenType.Keyword,
                    SemanticTokenType.Function,
                    SemanticTokenType.Variable,
                    SemanticTokenType.String,
                    SemanticTokenType.Number,
                    SemanticTokenType.Operator,
                    SemanticTokenType.Property,
                    SemanticTokenType.Regexp,     // Brackets stand-in
                    SemanticTokenType.Parameter,
                    SemanticTokenType.Comment),   // Added comment highlighting
                TokenModifiers = new Container<SemanticTokenModifier>(
                    SemanticTokenModifier.Declaration,
                    SemanticTokenModifier.Readonly,
                    SemanticTokenModifier.Static),
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = true,
        };
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams @params,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new SemanticTokensDocument(this.RegistrationOptions.Legend));
    }

    protected override Task Tokenize(
        SemanticTokensBuilder builder,
        ITextDocumentIdentifierParams identifier,
        CancellationToken cancellationToken)
    {
        var document = this.workspace.GetDocument(identifier.TextDocument.Uri);
        if (document?.LastParseResult?.Success != true)
        {
            return Task.CompletedTask;
        }

        // Collect all semantic spans first so we can sort (builder requires ordered pushes).
        var spans = new List<SemanticSpan>(256);

        void AddSpan(int start, int length, SemanticTokenType type)
        {
            if (length <= 0)
            {
                return;
            }

            spans.Add(new SemanticSpan(start, length, type));
        }

        var visitor = new SemanticTokenVisitor(document.Content, AddSpan);
        foreach (var statement in document.LastParseResult.Statements)
        {
            statement.Accept(visitor);
        }

        // Add comment tokens (now included in ordering). ParseResult.Comments must supply Token list.
        var comments = document.LastParseResult.Comments;
        if (comments != null)
        {
            foreach (var c in comments)
            {
                AddSpan(c.Start, c.Length, SemanticTokenType.Comment);
            }
        }

        // Sort by start position to satisfy LSP semantic token delta encoding.
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));

        foreach (var s in spans)
        {
            var (line, ch) = PositionHelper.GetLineChar(document.Content, s.Start);
            builder.Push(line, ch, s.Length, s.Type, System.Array.Empty<SemanticTokenModifier>());
        }

        return Task.CompletedTask;
    }

    private readonly record struct SemanticSpan(int Start, int Length, SemanticTokenType Type);

    private static class PositionHelper
    {
        public static (int Line, int Char) GetLineChar(string text, int offset)
        {
            offset = Math.Clamp(offset, 0, text.Length);
            int line = 0;
            int col = 0;
            for (int i = 0; i < offset; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    col = 0;
                }
                else
                {
                    col++;
                }
            }

            return (line, col);
        }
    }

    private class SemanticTokenVisitor : IAstVisitor
    {
        private readonly string text;
        private readonly Action<int, int, SemanticTokenType> addSpan;
        private readonly Dictionary<TokenType, SemanticTokenType> tokenTypeMap;

        public SemanticTokenVisitor(string text, Action<int, int, SemanticTokenType> addSpan)
        {
            this.text = text;
            this.addSpan = addSpan;
            this.tokenTypeMap = new Dictionary<TokenType, SemanticTokenType>
            {
                // Operators
                [TokenType.Plus] = SemanticTokenType.Operator,
                [TokenType.Minus] = SemanticTokenType.Operator,
                [TokenType.Multiply] = SemanticTokenType.Operator,
                [TokenType.Divide] = SemanticTokenType.Operator,
                [TokenType.Mod] = SemanticTokenType.Operator,
                [TokenType.Pow] = SemanticTokenType.Operator,
                [TokenType.Equal] = SemanticTokenType.Operator,
                [TokenType.NotEqual] = SemanticTokenType.Operator,
                [TokenType.LessThan] = SemanticTokenType.Operator,
                [TokenType.GreaterThan] = SemanticTokenType.Operator,
                [TokenType.LessThanOrEqual] = SemanticTokenType.Operator,
                [TokenType.GreaterThanOrEqual] = SemanticTokenType.Operator,
                [TokenType.And] = SemanticTokenType.Operator,
                [TokenType.Or] = SemanticTokenType.Operator,
                [TokenType.Xor] = SemanticTokenType.Operator,
                [TokenType.Not] = SemanticTokenType.Operator,
                [TokenType.Assignment] = SemanticTokenType.Operator,
                [TokenType.Pipe] = SemanticTokenType.Operator,

                // Brackets
                [TokenType.OpenBrace] = SemanticTokenType.Regexp,
                [TokenType.CloseBrace] = SemanticTokenType.Regexp,
                [TokenType.OpenBracket] = SemanticTokenType.Regexp,
                [TokenType.CloseBracket] = SemanticTokenType.Regexp,
                [TokenType.OpenParenthesis] = SemanticTokenType.Regexp,
                [TokenType.CloseParenthesis] = SemanticTokenType.Regexp,

                // Literals
                [TokenType.String] = SemanticTokenType.String,
                [TokenType.InterpolatedString] = SemanticTokenType.String,
                [TokenType.Number] = SemanticTokenType.Number,
                [TokenType.Decimal] = SemanticTokenType.Number,
            };
        }

        public void VisitToken(Token token)
        {
            if (this.tokenTypeMap.TryGetValue(token.Type, out var semanticType))
            {
                this.addSpan(token.Start, token.Length, semanticType);
            }
        }

        public void VisitKeyword(Token token)
        {
            this.addSpan(token.Start, token.Length, SemanticTokenType.Keyword);
        }

        public void Visit(CommandStatement commandStatement)
        {
            this.addSpan(commandStatement.CommandToken.Start, commandStatement.CommandToken.Length, SemanticTokenType.Function);
            foreach (var arg in commandStatement.Arguments)
            {
                arg.Accept(this);
            }
        }

        public void Visit(CommandExpression commandExpression)
        {
            this.addSpan(commandExpression.CommandToken.Start, commandExpression.CommandToken.Length, SemanticTokenType.Function);
            foreach (var arg in commandExpression.Arguments)
            {
                arg.Accept(this);
            }
        }

        public void Visit(CommandOption commandOption)
        {
            this.addSpan(commandOption.NameToken.Start, commandOption.NameToken.Length, SemanticTokenType.Parameter);
            commandOption.Value?.Accept(this);
        }

        public void Visit(ErrorExpression errorExpression)
        {
            // Intentionally no tokens
        }

        public void Visit(ConstantExpression constantExpression)
        {
            var start = constantExpression.Start;
            var length = constantExpression.Length;

            switch (constantExpression.Value.DataType)
            {
                case DataType.Text:
                    if (constantExpression.Value is ShellIdentifier id &&
                        (id.Value == "true" || id.Value == "false"))
                    {
                        this.addSpan(start, length, SemanticTokenType.Keyword);
                        return;
                    }

                    this.addSpan(start, length, SemanticTokenType.String);
                    break;

                case DataType.Number:
                case DataType.Decimal:
                    this.addSpan(start, length, SemanticTokenType.Number);
                    break;

                case DataType.Boolean:
                    this.addSpan(start, length, SemanticTokenType.Keyword);
                    break;
            }
        }

        public void Visit(VariableExpression variableExpression)
        {
            this.addSpan(variableExpression.VariableToken.Start, variableExpression.VariableToken.Length, SemanticTokenType.Variable);
        }

        public void Visit(InterpolatedStringExpression interpolatedStringExpression)
        {
            this.addSpan(interpolatedStringExpression.Start, interpolatedStringExpression.Length, SemanticTokenType.String);
            foreach (var expr in interpolatedStringExpression.Expressions)
            {
                expr.Accept(this);
            }
        }

        public void Visit(BinaryOperatorExpression binaryOperatorExpression)
        {
            binaryOperatorExpression.Left.Accept(this);
            this.VisitToken(binaryOperatorExpression.OperatorToken);
            binaryOperatorExpression.Right.Accept(this);
        }

        public void Visit(UnaryOperatorExpression unaryOperatorExpression)
        {
            this.VisitToken(unaryOperatorExpression.OperatorToken);
            unaryOperatorExpression.Expression.Accept(this);
        }

        public void Visit(JsonExpression jsonExpression)
        {
            // Opening brace
            this.addSpan(jsonExpression.Start, 1, SemanticTokenType.Regexp);

            foreach (var prop in jsonExpression.Properties)
            {
                if (prop.Key is ShellText keyText)
                {
                    var keyStr = keyText.Text;

                    // Naive lookup (could be improved by storing exact token positions in parser)
                    var keyIndex = this.text.IndexOf(keyStr, jsonExpression.Start, StringComparison.Ordinal);
                    if (keyIndex >= 0)
                    {
                        this.addSpan(keyIndex, keyStr.Length, SemanticTokenType.Property);
                    }
                }

                prop.Value.Accept(this);
            }

            // Closing brace
            this.addSpan(jsonExpression.Start + jsonExpression.Length - 1, 1, SemanticTokenType.Regexp);
        }

        public void Visit(JsonArrayExpression jsonArrayExpression)
        {
            this.addSpan(jsonArrayExpression.LBracketToken.Start, jsonArrayExpression.LBracketToken.Length, SemanticTokenType.Regexp);
            foreach (var expr in jsonArrayExpression.Expressions)
            {
                expr.Accept(this);
            }

            this.addSpan(jsonArrayExpression.RBracketToken.Start, jsonArrayExpression.RBracketToken.Length, SemanticTokenType.Regexp);
        }

        public void Visit(IfStatement ifStatement)
        {
            this.VisitKeyword(ifStatement.IfToken);
            ifStatement.Condition.Accept(this);
            ifStatement.Statement.Accept(this);
            ifStatement.ElseStatement?.Accept(this);
        }

        public void Visit(WhileStatement whileStatement)
        {
            this.VisitKeyword(whileStatement.WhileToken);
            whileStatement.Condition.Accept(this);
            whileStatement.Statement.Accept(this);
        }

        public void Visit(ForStatement forStatement)
        {
            this.VisitKeyword(forStatement.ForToken);
            this.addSpan(forStatement.VariableToken.Start, forStatement.VariableToken.Length, SemanticTokenType.Variable);
            this.VisitKeyword(forStatement.InToken);
            forStatement.Collection.Accept(this);
            forStatement.Statement.Accept(this);
        }

        public void Visit(BreakStatement breakStatement)
        {
            this.VisitKeyword(breakStatement.BreakToken);
        }

        public void Visit(ContinueStatement continueStatement)
        {
            this.VisitKeyword(continueStatement.ContinueToken);
        }

        public void Visit(ReturnStatement returnStatement)
        {
            this.VisitKeyword(returnStatement.ReturnToken);
            returnStatement.Value?.Accept(this);
        }

        public void Visit(AssignmentStatement s)
        {
            s.Variable.Accept(this);
            this.VisitToken(s.AssignmentToken);
            s.Value.Accept(this);
        }

        public void Visit(BlockStatement s)
        {
            foreach (var stmt in s.Statements)
            {
                stmt.Accept(this);
            }
        }

        public void Visit(DefStatement s)
        {
            this.VisitKeyword(s.DefToken);
            s.Statement.Accept(this);
        }

        public void Visit(DoWhileStatement s)
        {
            this.VisitKeyword(s.DoToken);
            s.Statement.Accept(this);
            this.VisitKeyword(s.WhileToken);
            s.Condition.Accept(this);
        }

        public void Visit(ExecStatement s)
        {
            this.VisitKeyword(s.ExecToken);
            s.CommandExpression.Accept(this);
            foreach (var arg in s.Arguments)
            {
                arg.Accept(this);
            }
        }

        public void Visit(LoopStatement s)
        {
            this.VisitKeyword(s.LoopToken);
            s.Statement.Accept(this);
        }

        public void Visit(PipeStatement s)
        {
            foreach (var stmt in s.Statements)
            {
                stmt.Accept(this);
            }
        }

        public void Visit(ParensExpression e)
        {
            e.InnerExpression.Accept(this);
        }

        public void Visit(JSonPathExpression e)
        {
            // If you later add precise token positions for JSON path segments,
            // emit them here.
        }
    }
}