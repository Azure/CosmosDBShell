// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System.Collections.Generic;
using System.Text;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp.Semantics;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Extensions.Logging;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal class CosmosShellHoverHandler : IHoverHandler
{
    // Statement keyword → base help key (without -description / -syntax / -example suffix)
    private static readonly Dictionary<string, string> StatementHelpKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["if"] = "statement-if",
        ["else"] = "statement-if",
        ["while"] = "statement-while",
        ["loop"] = "statement-loop",
        ["do"] = "statement-do",
        ["for"] = "statement-for",
        ["def"] = "statement-def",
        ["return"] = "statement-return",
        ["break"] = "statement-break",
        ["continue"] = "statement-continue",
        ["exec"] = "statement-exec",
    };

    private readonly CosmosShellWorkspace workspace;
    private readonly ILogger<CosmosShellHoverHandler> logger;

    public CosmosShellHoverHandler(
        CosmosShellWorkspace workspace,
        ILogger<CosmosShellHoverHandler> logger)
    {
        this.workspace = workspace;
        this.logger = logger;
    }

    public HoverRegistrationOptions GetRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = LspServer.DocumentSelector,
        };

    public Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        try
        {
            var doc = this.workspace.GetDocument(request.TextDocument.Uri);
            if (doc == null)
            {
                return Task.FromResult<Hover?>(null);
            }

            var offset = GetOffset(doc.Content, request.Position);

            // 1. FIRST check for statement keywords (before semantic model)
            var stmtKeywordToken = TryFindStatementKeywordToken(doc, offset);
            if (stmtKeywordToken != null &&
                StatementHelpKeys.TryGetValue(stmtKeywordToken.Value, out var baseKey))
            {
                var md = BuildStatementMarkdown(baseKey, stmtKeywordToken.Value);
                if (md != null)
                {
                    var range = ToRange(doc.Content, stmtKeywordToken.Start, stmtKeywordToken.Start + stmtKeywordToken.Value.Length);
                    return Task.FromResult<Hover?>(CreateHover(md, range));
                }
            }

            // 2. Then use semantic model for symbol hover (commands / variables / future kinds)
            var sem = doc.SemanticModel;
            if (sem != null)
            {
                var symbol = sem.GetSymbolAt(offset);
                if (symbol != null)
                {
                    var hover = CreateSymbolHover(symbol, doc);
                    if (hover != null)
                    {
                        return Task.FromResult<Hover?>(hover);
                    }
                }
            }

            return Task.FromResult<Hover?>(null);
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(ex, "Hover handler failed.");
            return Task.FromResult<Hover?>(null);
        }
    }

    private static Hover CreateHover(string markdown, Range range)
        => new()
        {
            Contents = new MarkedStringsOrMarkupContent(
                new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = markdown,
                }),
            Range = range,
        };

    private static string? SafeGetString(string key)
    {
        try
        {
            return MessageService.GetString(key);
        }
        catch
        {
            return null;
        }
    }

    // Strip Spectre.Console markup tags for LSP markdown
    private static string UnescapeMarkup(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        bool inTag = false;
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '[')
            {
                inTag = true;
                continue;
            }

            if (inTag && c == ']')
            {
                inTag = false;
                continue;
            }

            if (!inTag)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string BuildCommandMarkdown(CommandFactory factory)
    {
        var sb = new StringBuilder();
        var name = factory.CommandName;

        sb.AppendLine($"### `{name}`");
        if (!string.IsNullOrEmpty(factory.Description))
        {
            sb.AppendLine().AppendLine(factory.Description);
        }

        if (!string.IsNullOrEmpty(factory.McpDescription))
        {
            sb.AppendLine().AppendLine(factory.McpDescription);
        }

        sb.AppendLine();
        sb.Append(MessageService.GetString("hover-command-usage")).Append(" `").Append(name);

        if (factory.Options.Count > 0)
        {
            foreach (var opt in factory.Options)
            {
                var optName = opt.Name.FirstOrDefault();
                if (optName == null)
                {
                    continue;
                }

                var needsArg = !opt.PropertyInfo.PropertyType.IsAssignableFrom(typeof(bool));
                sb.Append(" [-").Append(optName);
                if (needsArg)
                {
                    sb.Append(" <ARG>");
                }

                sb.Append(']');
            }
        }

        foreach (var p in factory.Parameters)
        {
            var pName = p.Name.FirstOrDefault();
            if (pName == null)
            {
                continue;
            }

            if (p.IsRequired)
            {
                sb.Append(' ').Append(pName);
            }
            else
            {
                sb.Append(" [").Append(pName).Append(']');
            }
        }

        sb.Append('`').AppendLine();

        if (factory.Parameters.Count > 0)
        {
            sb.AppendLine().AppendLine(MessageService.GetString("hover-command-arguments"));
            foreach (var p in factory.Parameters)
            {
                var pName = p.Name.FirstOrDefault();
                if (pName == null)
                {
                    continue;
                }

                var desc = p.GetDescription(factory.CommandName) ?? string.Empty;
                sb.Append("- ");
                if (!p.IsRequired)
                {
                    sb.Append('[').Append(pName).Append("] ").Append(MessageService.GetString("hover-optional")).Append(": ");
                }
                else
                {
                    sb.Append('`').Append(pName).Append("`: ");
                }

                sb.AppendLine(string.IsNullOrWhiteSpace(desc) ? MessageService.GetString("hover-no-description") : desc);
            }
        }

        if (factory.Options.Count > 0)
        {
            sb.AppendLine().AppendLine(MessageService.GetString("hover-command-options"));
            foreach (var o in factory.Options)
            {
                var first = o.Name.FirstOrDefault();
                if (first == null)
                {
                    continue;
                }

                var optDesc = o.GetDescription(factory.CommandName) ?? string.Empty;
                var needsArg = !o.PropertyInfo.PropertyType.IsAssignableFrom(typeof(bool));
                sb.Append("- `-").Append(first);
                if (needsArg)
                {
                    sb.Append(" <ARG>");
                }

                sb.Append('`');
                if (o.Name.Length > 1)
                {
                    sb.Append(" (").Append(MessageService.GetString("hover-aliases")).Append(": ");
                    sb.Append(string.Join(", ", o.Name.Skip(1).Select(a => "-" + a)));
                    sb.Append(')');
                }

                sb.Append(": ").AppendLine(string.IsNullOrWhiteSpace(optDesc) ? MessageService.GetString("hover-no-description") : optDesc);
            }
        }

        if (factory.Examples.Count > 0)
        {
            sb.AppendLine().AppendLine(MessageService.GetString("hover-command-examples"));
            foreach (var ex in factory.Examples)
            {
                sb.AppendLine("```");
                sb.AppendLine(ex);
                sb.AppendLine("```");
            }
        }

        if (factory.McpRestricted)
        {
            sb.AppendLine().AppendLine("> ⚠️ " + MessageService.GetString("hover-command-restricted-warning"));
        }

        return sb.ToString().TrimEnd();
    }

    private static string? BuildStatementMarkdown(string baseKey, string keyword)
    {
        var desc = SafeGetString($"{baseKey}-description");
        var syntax = SafeGetString($"{baseKey}-syntax");
        var example = SafeGetString($"{baseKey}-example");

        if (desc == null && syntax == null && example == null)
        {
            return null;
        }

        var title = MessageService.GetString(
            "hover-statement-title",
            MessageService.Args("keyword", keyword));

        var sb = new StringBuilder();
        sb.AppendLine("### " + title);

        if (desc != null)
        {
            sb.AppendLine().AppendLine(desc);
        }

        if (syntax != null)
        {
            sb.AppendLine().AppendLine(MessageService.GetString("hover-syntax"));
            sb.AppendLine("```");
            sb.AppendLine(UnescapeMarkup(syntax));
            sb.AppendLine("```");
        }

        if (example != null)
        {
            sb.AppendLine().AppendLine(MessageService.GetString("hover-example"));
            sb.AppendLine("```");
            sb.AppendLine(UnescapeMarkup(example));
            sb.AppendLine("```");
        }

        return sb.ToString().TrimEnd();
    }

    private static (int Line, int Column) ToLineColumn(string content, int absolute)
    {
        absolute = Math.Clamp(absolute, 0, content.Length);
        int line = 0;
        int lastNl = -1;
        for (int i = 0; i < absolute; i++)
        {
            if (content[i] == '\n')
            {
                line++;
                lastNl = i;
            }
        }

        int column = absolute - (lastNl + 1);
        return (line, column);
    }

    private static Range ToRange(string content, int start, int end)
    {
        var (sl, sc) = ToLineColumn(content, start);
        var (el, ec) = ToLineColumn(content, Math.Max(start, end - 1));
        return new Range(new Position(sl, sc), new Position(el, ec + 1));
    }

    private static int GetOffset(string content, Position position)
    {
        var lines = content.Split('\n');
        int offset = 0;
        for (int i = 0; i < position.Line && i < lines.Length; i++)
        {
            offset += lines[i].Length + 1; // include newline
        }

        if (position.Line < lines.Length)
        {
            offset += Math.Min(position.Character, lines[position.Line].Length);
        }

        return offset;
    }

    private static Token? TryFindStatementKeywordToken(WorkspaceDocument doc, int offset)
    {
        var parse = doc.LastParseResult;
        if (parse?.Statements == null)
        {
            return null;
        }

        foreach (var st in EnumerateStatements(parse.Statements))
        {
            switch (st)
            {
                case IfStatement ifs:
                    if (IsInToken(ifs.IfToken, offset))
                    {
                        return ifs.IfToken;
                    }

                    if (ifs.ElseToken != null && IsInToken(ifs.ElseToken, offset))
                    {
                        return ifs.ElseToken;
                    }

                    break;
                case WhileStatement ws:
                    if (IsInToken(ws.WhileToken, offset))
                    {
                        return ws.WhileToken;
                    }

                    break;
                case LoopStatement ls:
                    if (IsInToken(ls.LoopToken, offset))
                    {
                        return ls.LoopToken;
                    }

                    break;
                case ForStatement fors:
                    if (IsInToken(fors.ForToken, offset))
                    {
                        return fors.ForToken;
                    }

                    if (IsInToken(fors.InToken, offset))
                    {
                        return fors.InToken;
                    }

                    break;
                case DefStatement defs:
                    if (IsInToken(defs.DefToken, offset))
                    {
                        return defs.DefToken;
                    }

                    break;
                case ReturnStatement rs:
                    if (IsInToken(rs.ReturnToken, offset))
                    {
                        return rs.ReturnToken;
                    }

                    break;
                case BreakStatement bs:
                    if (IsInToken(bs.BreakToken, offset))
                    {
                        return bs.BreakToken;
                    }

                    break;
                default:
                    if (st.GetType().Name.Equals("ContinueStatement", StringComparison.OrdinalIgnoreCase))
                    {
                        var prop = st.GetType().GetProperty("ContinueToken");
                        if (prop?.GetValue(st) is Token continueTok && IsInToken(continueTok, offset))
                        {
                            return continueTok;
                        }
                    }

                    if (st.GetType().Name.Equals("DoWhileStatement", StringComparison.OrdinalIgnoreCase))
                    {
                        var doProp = st.GetType().GetProperty("DoToken");
                        var whileProp = st.GetType().GetProperty("WhileToken");
                        if (doProp?.GetValue(st) is Token doTok && IsInToken(doTok, offset))
                        {
                            return doTok;
                        }

                        if (whileProp?.GetValue(st) is Token whileTok && IsInToken(whileTok, offset))
                        {
                            return whileTok;
                        }
                    }

                    break;
            }
        }

        return null;
    }

    private static bool IsInToken(Token token, int offset)
    {
        var start = token.Start;
        var len = token.Value?.Length ?? 0;
        return offset >= start && offset < start + len;
    }

    private static IEnumerable<Statement> EnumerateStatements(IEnumerable<Statement> root)
    {
        foreach (var st in root)
        {
            yield return st;

            if (st is BlockStatement block)
            {
                foreach (var inner in EnumerateStatements(block.Statements))
                {
                    yield return inner;
                }
            }

            if (st is IfStatement ifs)
            {
                foreach (var nested in EnumeratePossibleChild(ifs.Statement))
                {
                    yield return nested;
                }

                if (ifs.ElseStatement != null)
                {
                    foreach (var nested in EnumeratePossibleChild(ifs.ElseStatement))
                    {
                        yield return nested;
                    }
                }
            }

            if (st is WhileStatement ws)
            {
                foreach (var nested in EnumeratePossibleChild(ws.Statement))
                {
                    yield return nested;
                }
            }

            if (st is LoopStatement ls)
            {
                foreach (var nested in EnumeratePossibleChild(ls.Statement))
                {
                    yield return nested;
                }
            }

            if (st is ForStatement fs)
            {
                foreach (var nested in EnumeratePossibleChild(fs.Statement))
                {
                    yield return nested;
                }
            }

            if (st is DefStatement ds)
            {
                foreach (var nested in EnumeratePossibleChild(ds.Statement))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<Statement> EnumeratePossibleChild(Statement stmt)
    {
        if (stmt is BlockStatement block)
        {
            foreach (var s in EnumerateStatements(block.Statements))
            {
                yield return s;
            }
        }
        else
        {
            yield return stmt;
        }
    }

    private static Hover? CreateSymbolHover(Symbol symbol, WorkspaceDocument doc)
    {
        switch (symbol.Kind)
        {
            case Semantics.SymbolKind.Command:
                if (ShellInterpreter.Instance.App.Commands.TryGetValue(symbol.Name, out var factory))
                {
                    var md = BuildCommandMarkdown(factory);
                    var range = ToRange(doc.Content, symbol.Start, symbol.Start + symbol.Length);
                    return CreateHover(md, range);
                }
                else
                {
                    var md = "### " + MessageService.GetString(
                        "hover-command-unknown",
                        MessageService.Args("name", symbol.Name));
                    var range = ToRange(doc.Content, symbol.Start, symbol.Start + symbol.Length);
                    return CreateHover(md, range);
                }

            case Semantics.SymbolKind.Variable:
                {
                    var refs = doc.SemanticModel?
                        .FindReferences(symbol)
                        .ToList() ?? [];
                    var def = refs.FirstOrDefault(r => r.IsDefinition);
                    var (dl, dc) = def != null ? ToLineColumn(doc.Content, def.Start) : (-1, -1);

                    var mdBuilder = new StringBuilder();
                    mdBuilder.AppendLine("### " + MessageService.GetString(
                        "hover-variable-title",
                        MessageService.Args("name", symbol.Name)));

                    if (dl >= 0)
                    {
                        mdBuilder.AppendLine()
                                 .AppendLine(MessageService.GetString(
                                     "hover-variable-defined",
                                     MessageService.Args("line", dl + 1, "column", dc + 1)));
                    }

                    mdBuilder.AppendLine()
                             .AppendLine(MessageService.GetString(
                                 "hover-variable-references",
                                 MessageService.Args("count", refs.Count)));

                    var range = ToRange(doc.Content, symbol.Start, symbol.Start + symbol.Length);
                    return CreateHover(mdBuilder.ToString().TrimEnd(), range);
                }

            case Semantics.SymbolKind.Function:
                {
                    var md = "### " + MessageService.GetString(
                        "hover-function-title",
                        MessageService.Args("name", symbol.Name));
                    var range = ToRange(doc.Content, symbol.Start, symbol.Start + symbol.Length);
                    return CreateHover(md, range);
                }

            default:
                return null;
        }
    }
}