// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Extensions.Logging;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal class CosmosShellFormattingHandler : IDocumentFormattingHandler, IDocumentRangeFormattingHandler
{
    private readonly CosmosShellWorkspace workspace;
    private readonly ILogger<CosmosShellFormattingHandler> logger;

    public CosmosShellFormattingHandler(
        CosmosShellWorkspace workspace,
        ILogger<CosmosShellFormattingHandler> logger)
    {
        this.workspace = workspace;
        this.logger = logger;
    }

    public DocumentFormattingRegistrationOptions GetRegistrationOptions(
        DocumentFormattingCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = LspServer.DocumentSelector,
        };

    public DocumentRangeFormattingRegistrationOptions GetRegistrationOptions(
        DocumentRangeFormattingCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = LspServer.DocumentSelector,
        };

    public Task<TextEditContainer?> Handle(
        DocumentFormattingParams request,
        CancellationToken cancellationToken)
    {
        try
        {
            var doc = this.workspace.GetDocument(request.TextDocument.Uri);
            if (doc == null)
            {
                return Task.FromResult<TextEditContainer?>(null);
            }

            var edits = FormatDocument(doc, request.Options);
            return Task.FromResult<TextEditContainer?>(
                edits.Count > 0 ? new TextEditContainer(edits) : null);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Document formatting failed");
            return Task.FromResult<TextEditContainer?>(null);
        }
    }

    public Task<TextEditContainer> Handle(
        DocumentRangeFormattingParams request,
        CancellationToken cancellationToken)
    {
        try
        {
            var doc = this.workspace.GetDocument(request.TextDocument.Uri);
            if (doc == null)
            {
                return Task.FromResult(new TextEditContainer());
            }

            var edits = FormatRange(doc, request.Range, request.Options);
            return Task.FromResult(new TextEditContainer(edits));
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Range formatting failed");
            return Task.FromResult(new TextEditContainer());
        }
    }

    public void SetCapability(DocumentFormattingCapability capability)
    {
    }

    public void SetCapability(DocumentRangeFormattingCapability capability)
    {
    }

    private static List<TextEdit> FormatDocument(WorkspaceDocument document, FormattingOptions options)
    {
        var formatter = new CosmosShellFormatter(document.Content, options, document.LastParseResult);
        return formatter.GetEdits();
    }

    private static List<TextEdit> FormatRange(WorkspaceDocument document, Range range, FormattingOptions options)
    {
        var formatter = new CosmosShellFormatter(document.Content, options, document.LastParseResult);
        return formatter.GetEdits(range);
    }

    private class CosmosShellFormatter
    {
        private readonly string content;
        private readonly string[] lines;
        private readonly FormattingOptions options;
        private readonly string indentString;
        private readonly ParseResult? parseResult;
        private readonly Dictionary<int, Statement> lineToStatement = new();
        private readonly Dictionary<Statement, int> statementIndentLevel = new();

        public CosmosShellFormatter(string content, FormattingOptions options, ParseResult? existingParse)
        {
            this.content = content;
            this.lines = content.Split('\n');
            this.options = options;
            this.indentString = options.InsertSpaces
                ? new string(' ', (int)options.TabSize)
                : "\t";

            if (existingParse != null && existingParse.Success && existingParse.Errors.Count == 0)
            {
                this.parseResult = existingParse;
                this.AnalyzeStructure();
            }
            else
            {
                var lexer = new Lexer(content);
                var parser = new StatementParser(lexer);
                var statements = parser.ParseStatements();
                if (lexer.Errors.Count == 0)
                {
                    this.parseResult = new ParseResult
                    {
                        Success = true,
                        Statements = statements,
                        Comments = lexer.Comments,
                    };
                    this.AnalyzeStructure();
                }
            }
        }

        public List<TextEdit> GetEdits(Range? range = null)
        {
            var edits = new List<TextEdit>();
            var startLine = range != null ? (int)range.Start.Line : 0;
            var endLine = range != null ? Math.Min((int)range.End.Line, this.lines.Length - 1) : this.lines.Length - 1;

            for (int lineNum = startLine; lineNum <= endLine; lineNum++)
            {
                var edit = this.GetLineEdit(lineNum);
                if (edit != null)
                {
                    edits.Add(edit);
                }
            }

            return edits;
        }

        private TextEdit? GetLineEdit(int lineNum)
        {
            if (lineNum >= this.lines.Length)
            {
                return null;
            }

            var originalLine = this.lines[lineNum];
            var trimmed = originalLine.TrimStart();

            if (string.IsNullOrEmpty(trimmed))
            {
                if (originalLine.Length > 0)
                {
                    return new TextEdit
                    {
                        Range = new Range(new Position(lineNum, 0), new Position(lineNum, originalLine.Length)),
                        NewText = string.Empty,
                    };
                }

                return null;
            }

            var indentLevel = this.CalculateIndentLevel(lineNum, trimmed);
            var properIndent = this.GetIndentString(indentLevel);
            var currentIndent = originalLine[..(originalLine.Length - trimmed.Length)];

            if (currentIndent != properIndent)
            {
                return new TextEdit
                {
                    Range = new Range(new Position(lineNum, 0), new Position(lineNum, currentIndent.Length)),
                    NewText = properIndent,
                };
            }

            return null;
        }

        private int CalculateIndentLevel(int lineNum, string trimmedLine)
        {
            if (this.parseResult != null && this.lineToStatement.TryGetValue(lineNum, out var stmt))
            {
                // Check if this is a closing brace line
                if (trimmedLine.StartsWith('}'))
                {
                    // Look for the ClosingBraceMarker entry to get the correct indent
                    foreach (var kvp in this.statementIndentLevel)
                    {
                        if (kvp.Key is IndentAnalyzer.ClosingBraceMarker marker &&
                            marker.Start == stmt.Start && marker.Length == stmt.Length)
                        {
                            return kvp.Value;
                        }
                    }
                }

                // Regular statement indent
                if (this.statementIndentLevel.TryGetValue(stmt, out var level))
                {
                    return level;
                }
            }

            return this.CalculateHeuristicIndent(lineNum, trimmedLine);
        }

        private int CalculateHeuristicIndent(int lineNum, string trimmedLine)
        {
            int currentIndent = 0;

            for (int i = lineNum - 1; i >= 0; i--)
            {
                var prevLine = this.lines[i].Trim();
                if (string.IsNullOrEmpty(prevLine))
                {
                    continue;
                }

                currentIndent = this.CountIndentLevel(this.lines[i]);

                if (prevLine.StartsWith("if ", StringComparison.OrdinalIgnoreCase) ||
                    prevLine.StartsWith("while ", StringComparison.OrdinalIgnoreCase) ||
                    prevLine.StartsWith("for ", StringComparison.OrdinalIgnoreCase) ||
                    prevLine.StartsWith("do", StringComparison.OrdinalIgnoreCase) ||
                    prevLine.StartsWith("loop", StringComparison.OrdinalIgnoreCase) ||
                    prevLine.StartsWith("def ", StringComparison.OrdinalIgnoreCase) ||
                    prevLine.EndsWith('{'))
                {
                    currentIndent++;
                }

                break;
            }

            if (trimmedLine.StartsWith('}'))
            {
                currentIndent = Math.Max(0, currentIndent - 1);
            }

            return currentIndent;
        }

        private void AnalyzeStructure()
        {
            if (this.parseResult == null)
            {
                return;
            }

            var visitor = new IndentAnalyzer(this.content, this.lineToStatement, this.statementIndentLevel);
            foreach (var stmt in this.parseResult.Statements)
            {
                visitor.AnalyzeStatement(stmt, 0);
            }
        }

        private int CountIndentLevel(string line)
        {
            int count = 0;
            int i = 0;

            if (this.options.InsertSpaces)
            {
                while (i < line.Length && line[i] == ' ')
                {
                    i++;
                }

                count = i / (int)this.options.TabSize;
            }
            else
            {
                while (i < line.Length && line[i] == '\t')
                {
                    count++;
                    i++;
                }
            }

            return count;
        }

        private string GetIndentString(int level)
            => string.Concat(Enumerable.Repeat(this.indentString, level));

        private class IndentAnalyzer
        {
            private readonly string content;
            private readonly Dictionary<int, Statement> lineToStatement;
            private readonly Dictionary<Statement, int> statementIndentLevel;
            private readonly int[] lineStarts;

            public IndentAnalyzer(
                string content,
                Dictionary<int, Statement> lineToStatement,
                Dictionary<Statement, int> statementIndentLevel)
            {
                this.content = content;
                this.lineToStatement = lineToStatement;
                this.statementIndentLevel = statementIndentLevel;
                this.lineStarts = BuildLineStarts(content);
            }

            public void AnalyzeStatement(Statement stmt, int indentLevel)
            {
                var line = this.GetLineNumber(stmt.Start);

                // Store the statement's line mapping and indent level
                if (line >= 0)
                {
                    if (!this.lineToStatement.ContainsKey(line))
                    {
                        this.lineToStatement[line] = stmt;
                    }

                    // Always store the indent level for the statement
                    if (!this.statementIndentLevel.ContainsKey(stmt))
                    {
                        this.statementIndentLevel[stmt] = indentLevel;
                    }
                }

                switch (stmt)
                {
                    case BlockStatement block:
                        // Check if opening brace is on its own line
                        var openBraceLine = this.GetLineNumber(stmt.Start);
                        if (openBraceLine >= 0)
                        {
                            var lineContent = this.GetLineContent(openBraceLine).Trim();
                            if (lineContent.StartsWith('{'))
                            {
                                // Opening brace on its own line - use current indent level
                                this.lineToStatement[openBraceLine] = block;
                                this.statementIndentLevel[block] = indentLevel;
                            }
                        }

                        foreach (var child in block.Statements)
                        {
                            this.AnalyzeStatement(child, indentLevel + 1);
                        }

                        // Store closing brace line with the same indent as opening
                        var closeLine = this.GetLineNumber(stmt.Start + stmt.Length - 1);
                        if (closeLine >= 0 && closeLine != openBraceLine)
                        {
                            this.lineToStatement[closeLine] = block;

                            // Mark this as a closing brace line by using a special marker
                            this.statementIndentLevel[new ClosingBraceMarker(block)] = indentLevel;
                        }

                        break;

                    case IfStatement ifStmt:
                        // Check if the child statement is a block - if so, don't add extra indent
                        var ifChildIndent = ifStmt.Statement is BlockStatement ? indentLevel : indentLevel + 1;
                        this.AnalyzeStatement(ifStmt.Statement, ifChildIndent);

                        if (ifStmt.ElseStatement != null)
                        {
                            if (ifStmt.ElseToken != null)
                            {
                                var elseLine = this.GetLineNumber(ifStmt.ElseToken.Start);
                                if (elseLine >= 0 && !this.lineToStatement.ContainsKey(elseLine))
                                {
                                    this.lineToStatement[elseLine] = ifStmt;
                                    this.statementIndentLevel[ifStmt] = indentLevel;
                                }
                            }

                            // Check if else statement is a block
                            var elseChildIndent = ifStmt.ElseStatement is BlockStatement ? indentLevel : indentLevel + 1;
                            this.AnalyzeStatement(ifStmt.ElseStatement, elseChildIndent);
                        }

                        break;

                    case WhileStatement whileStmt:
                        // Check if the child statement is a block - if so, don't add extra indent
                        var whileChildIndent = whileStmt.Statement is BlockStatement ? indentLevel : indentLevel + 1;
                        this.AnalyzeStatement(whileStmt.Statement, whileChildIndent);
                        break;

                    case ForStatement forStmt:
                        // Check if the child statement is a block - if so, don't add extra indent
                        var forChildIndent = forStmt.Statement is BlockStatement ? indentLevel : indentLevel + 1;
                        this.AnalyzeStatement(forStmt.Statement, forChildIndent);
                        break;

                    case DoWhileStatement doWhileStmt:
                        // Check if the child statement is a block - if so, don't add extra indent
                        var doChildIndent = doWhileStmt.Statement is BlockStatement ? indentLevel : indentLevel + 1;
                        this.AnalyzeStatement(doWhileStmt.Statement, doChildIndent);

                        var whileLine = this.GetLineNumber(doWhileStmt.WhileToken.Start);
                        if (whileLine >= 0 && !this.lineToStatement.ContainsKey(whileLine))
                        {
                            this.lineToStatement[whileLine] = doWhileStmt;
                            this.statementIndentLevel[doWhileStmt] = indentLevel;
                        }

                        break;

                    case LoopStatement loopStmt:
                        // Check if the child statement is a block - if so, don't add extra indent
                        var loopChildIndent = loopStmt.Statement is BlockStatement ? indentLevel : indentLevel + 1;
                        this.AnalyzeStatement(loopStmt.Statement, loopChildIndent);
                        break;

                    case DefStatement defStmt:
                        // Check if the child statement is a block - if so, don't add extra indent
                        var defChildIndent = defStmt.Statement is BlockStatement ? indentLevel : indentLevel + 1;
                        this.AnalyzeStatement(defStmt.Statement, defChildIndent);
                        break;

                    case PipeStatement pipeStmt:
                        foreach (var part in pipeStmt.Statements)
                        {
                            this.AnalyzeStatement(part, indentLevel);
                        }

                        break;
                }
            }

            private static int[] BuildLineStarts(string text)
            {
                var list = new List<int> { 0 };
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        list.Add(i + 1);
                    }
                }

                return list.ToArray();
            }

            private string GetLineContent(int lineNumber)
            {
                if (lineNumber >= this.lineStarts.Length - 1)
                {
                    return this.content[this.lineStarts[lineNumber]..];
                }

                var start = this.lineStarts[lineNumber];
                var end = this.lineStarts[lineNumber + 1] - 1;
                if (end > start && this.content[end - 1] == '\r')
                {
                    end--;
                }

                return this.content[start..end];
            }

            private int GetLineNumber(int position)
            {
                int lo = 0;
                int hi = this.lineStarts.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    int start = this.lineStarts[mid];
                    int nextStart = mid + 1 < this.lineStarts.Length ? this.lineStarts[mid + 1] : this.content.Length + 1;
                    if (position < start)
                    {
                        hi = mid - 1;
                    }
                    else if (position >= nextStart)
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        return mid;
                    }
                }

                return this.lineStarts.Length - 1;
            }

            // Marker class to distinguish closing brace entries
            public class ClosingBraceMarker : Statement
            {
                private readonly Statement block;

                public ClosingBraceMarker(Statement block) => this.block = block;

                public override int Start => this.block.Start;

                public override int Length => this.block.Length;

                public override Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
                    => throw new NotImplementedException();

                internal override void Accept(IAstVisitor visitor) => throw new NotImplementedException();
            }
        }
    }
}