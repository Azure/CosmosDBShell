// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System.Collections.Concurrent;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Extensions.Logging;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal class CosmosShellCompletionHandler : ICompletionHandler
{
    private static readonly string[] StatementKeywords =
        ["if", "else", "while", "for", "do", "loop", "def", "return", "break", "continue"];

    private static readonly char[] Separator = [' ', '\t'];

    private readonly CosmosShellWorkspace workspace;
    private readonly ILogger<CosmosShellCompletionHandler> logger;
    private readonly ConcurrentDictionary<string, (DateTime Ts, CompletionList List)> cache = new();

    private readonly CompletionRegistrationOptions registrationOptions = new()
    {
        DocumentSelector = LspServer.DocumentSelector,
        TriggerCharacters = new Container<string>(" ", "|", "-", "$", "[", "\""),
        AllCommitCharacters = new Container<string>(" ", "\t"),
    };

    private CompletionCapability capability = null!;

    public CosmosShellCompletionHandler(
        CosmosShellWorkspace workspace,
        ILogger<CosmosShellCompletionHandler> logger)
    {
        this.workspace = workspace;
        this.logger = logger;
    }

    public static Task<CompletionItem> HandleAsync(CompletionItem request, CancellationToken cancellationToken)
    => Task.FromResult(request);

    public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
        => this.registrationOptions;

    public async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"{request.TextDocument.Uri}:{request.Position.Line}:{request.Position.Character}";
            if (this.cache.TryGetValue(key, out var entry) && (DateTime.UtcNow - entry.Ts).TotalSeconds < 1)
            {
                return entry.List;
            }

            var ctx = this.workspace.GetCompletionContext(request.TextDocument.Uri, request.Position);
            var partial = (ctx.GetCurrentPartial() ?? string.Empty).Trim();
            var items = new List<CompletionItem>();

            if (ctx.IsCommandPosition)
            {
                AddStatementKeywordCompletions(items, partial);
                AddCommandCompletions(items, partial);
            }
            else
            {
                // Try to determine if we are inside a command
                AddVariableCompletions(items, partial);
                var commandName = GetCurrentLineFirstToken(ctx.TextUpToPosition);
                if (!string.IsNullOrEmpty(commandName) &&
                    ShellInterpreter.Instance.App.Commands.TryGetValue(commandName, out var factory))
                {
                    AddOptionCompletions(items, factory, ctx.TextUpToPosition, partial);
                    AddParameterCompletions(items, factory, ctx.TextUpToPosition, partial);
                }
                else
                {
                    // Fallback to commands + keywords if user starts a new token
                    if (partial.Length == 0)
                    {
                        AddStatementKeywordCompletions(items, partial);
                        AddCommandCompletions(items, partial);
                    }
                }
            }

            var list = new CompletionList(items, false);
            this.cache[key] = (DateTime.UtcNow, list);
            return await Task.FromResult(list);
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(ex, "Completion handler failed.");
            return new CompletionList();
        }
    }

    public void SetCapability(CompletionCapability capability) => this.capability = capability;

    private static void AddStatementKeywordCompletions(List<CompletionItem> items, string partial)
    {
        foreach (var k in StatementKeywords.Where(k => k.StartsWith(partial, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new CompletionItem
            {
                Label = k,
                Kind = CompletionItemKind.Keyword,
                InsertText = k,
                SortText = "1_" + k,
            });
        }
    }

    private static void AddCommandCompletions(List<CompletionItem> items, string partial)
    {
        foreach (var (name, cmd) in ShellInterpreter.Instance.App.Commands
                     .Where(kv => kv.Key.StartsWith(partial, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Function,
                Detail = cmd.Description,
                InsertText = name,
                SortText = "2_" + name,
                Documentation = string.IsNullOrEmpty(cmd.McpDescription) ? null : new MarkupContent { Kind = MarkupKind.Markdown, Value = cmd.McpDescription },
            });
        }
    }

    private static void AddVariableCompletions(List<CompletionItem> items, string partial)
    {
        if (!partial.StartsWith('$') && partial.Length > 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in ShellInterpreter.Instance.VariableContainers.Reverse())
        {
            foreach (var name in container.Variables.Keys)
            {
                string variableName = "$" + name;
                if (seen.Add(name) && variableName.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new CompletionItem
                    {
                        Label = variableName,
                        Kind = CompletionItemKind.Variable,
                        InsertText = variableName,
                        SortText = "0_" + name,
                    });
                }
            }
        }
    }

    private static void AddOptionCompletions(List<CompletionItem> items, CommandFactory factory, string textUpToPosition, string partial)
    {
        var used = GetAlreadyUsedOptions(textUpToPosition);

        // Detect case where we're still on an option token that already includes an inline value (-opt:value)
        bool inlineValueMode = partial.StartsWith('-') && partial.Contains(':');

        foreach (var opt in factory.Options)
        {
            var primary = opt.Name.FirstOrDefault();
            if (primary == null)
            {
                continue;
            }

            if (used.Contains(primary))
            {
                // Already used -> never propose again
                continue;
            }

            if (partial.Length > 0)
            {
                if (partial.StartsWith('-'))
                {
                    // Strip leading '-' then split at ':' (so -max:10 is treated as -max for prefix logic)
                    var coreRaw = partial.TrimStart('-');
                    var core = coreRaw.Split(':')[0];

                    if (!inlineValueMode)
                    {
                        // Normal prefix filtering while typing the option name
                        if (!opt.Name.Any(n => n.StartsWith(core, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // Inline value mode (e.g. "-max:10" with caret still before trailing space):
                        // treat current option as finished and allow OTHER options to appear (so user
                        // can see suggestions even if the editor caret is still before the final space).
                        // No additional filtering required here.
                    }
                }
                else
                {
                    // User did not start with '-' yet; only suggest options if partial matches an alias
                    if (!opt.Name.Any(n => n.StartsWith(partial, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                }
            }

            bool needsValue = !opt.PropertyInfo.PropertyType.IsAssignableFrom(typeof(bool));
            var insert = "-" + primary + (needsValue ? " " : string.Empty);

            items.Add(new CompletionItem
            {
                Label = "-" + primary,
                Kind = CompletionItemKind.Field,
                Detail = opt.GetDescription(factory.CommandName),
                InsertText = insert,
                SortText = "3_" + primary,
                Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"**Option** -{primary}\n\n{opt.GetDescription(factory.CommandName) ?? string.Empty}",
                },
            });
        }
    }

    private static void AddParameterCompletions(List<CompletionItem> items, CommandFactory factory, string textUpToPosition, string partial)
    {
        var (nonOptionArgsCount, isOptionValuePending) = CountProvidedArguments(factory, textUpToPosition);
        if (isOptionValuePending)
        {
            return; // user is after an option expecting a value
        }

        // Determine next parameter
        if (nonOptionArgsCount >= factory.Parameters.Count)
        {
            return;
        }

        var param = factory.Parameters[nonOptionArgsCount];
        var pname = param.Name.FirstOrDefault();
        if (pname == null)
        {
            return;
        }

        if (partial.Length > 0 && !pname.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var snippet = pname;
        items.Add(new CompletionItem
        {
            Label = pname,
            Kind = CompletionItemKind.Field,
            Detail = param.GetDescription(factory.CommandName),
            InsertText = snippet,
            SortText = "4_" + pname,
            Documentation = new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = $"**Parameter** {pname}\n\n{param.GetDescription(factory.CommandName) ?? string.Empty}",
            },
        });
    }

    private static HashSet<string> GetAlreadyUsedOptions(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in TokenizeLine(text))
        {
            if (token.Length > 1 && token[0] == '-' && token[1] != '-')
            {
                // only take first char sequence until space or colon
                var opt = token[1..].Split(':', ' ', '\t')[0];
                if (opt.Length > 0)
                {
                    result.Add(opt);
                }
            }
        }

        return result;
    }

    private static (int NonOptionArgs, bool OptionValuePending) CountProvidedArguments(CommandFactory factory, string text)
    {
        var tokens = TokenizeLine(text).ToList();
        if (tokens.Count == 0)
        {
            return (0, false);
        }

        // remove leading command
        tokens = [.. tokens.Skip(1)];
        int args = 0;
        bool pending = false;

        foreach (var t in tokens)
        {
            if (string.IsNullOrWhiteSpace(t))
            {
                continue;
            }

            if (t.StartsWith('-'))
            {
                // Option
                var core = t.TrimStart('-');
                if (core.Length == 0)
                {
                    continue;
                }

                var opt = factory.Options.FirstOrDefault(o => o.Name.Any(n => n.Equals(core, StringComparison.OrdinalIgnoreCase)));
                if (opt != null)
                {
                    if (!opt.PropertyInfo.PropertyType.IsAssignableFrom(typeof(bool)))
                    {
                        // Next token should be its value
                        pending = true;
                    }
                    else
                    {
                        pending = false;
                    }
                }

                continue;
            }

            if (pending)
            {
                // This token consumed as option value
                pending = false;
                continue;
            }

            // Regular argument
            args++;
        }

        return (args, pending);
    }

    private static string GetCurrentLineFirstToken(string textUpToPosition)
    {
        var lastNl = textUpToPosition.LastIndexOf('\n');
        var line = lastNl >= 0 ? textUpToPosition[(lastNl + 1)..] : textUpToPosition;
        var parts = line.TrimStart().Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static IEnumerable<string> TokenizeLine(string text)
    {
        // Very lightweight tokenizer (space separated, honoring simple quotes)
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}