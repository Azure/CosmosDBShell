// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Lsp;

using System.Collections.Concurrent;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Context information for completion requests.
/// </summary>
public class CompletionContext
{
    /// <summary>
    /// An empty <see cref="CompletionContext"/> instance.
    /// </summary>
    public static readonly CompletionContext Empty = new();

    /// <summary>
    /// Gets the text up to the current cursor position.
    /// </summary>
    public string TextUpToPosition { get; init; } = string.Empty;

    /// <summary>
    /// Gets the current statement at the cursor position, if any.
    /// </summary>
    public Statement? CurrentStatement { get; init; }

    /// <summary>
    /// Gets the position of the cursor in the document.
    /// </summary>
    public Position? Position { get; init; }

    /// <summary>
    /// Gets the workspace document associated with the current completion context, if any.
    /// </summary>
    public WorkspaceDocument? Document { get; init; }

    /// <summary>
    /// Determines if the caret is at a command position (start of a new command),
    /// including while typing the first token of a line (e.g. partial "qu" -> should propose commands).
    /// </summary>
    public bool IsCommandPosition
    {
        get
        {
            var txt = this.TextUpToPosition;
            if (string.IsNullOrWhiteSpace(txt))
            {
                return true;
            }

            var trimmedEnd = txt.TrimEnd();
            if (trimmedEnd.EndsWith("|") || trimmedEnd.EndsWith(";"))
            {
                return true;
            }

            // Current line text
            var lastNl = txt.LastIndexOf('\n');
            var line = lastNl >= 0 ? txt[(lastNl + 1)..] : txt;

            // First token in line (no whitespace yet) counts as command position unless it's clearly an option/variable.
            if (line.Length > 0 && line.IndexOfAny([' ', '\t']) < 0)
            {
                if (!line.StartsWith('$') && !line.StartsWith('-'))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets the current partial command or argument at the cursor position.
    /// Returns empty when cursor is after a separating whitespace (new token about to start).
    /// </summary>
    public string GetCurrentPartial()
    {
        // If the character immediately before the cursor is whitespace, we are at a new token.
        if (this.TextUpToPosition.EndsWith(' ') || this.TextUpToPosition.EndsWith('\t'))
        {
            return string.Empty;
        }

        var trimmed = this.TextUpToPosition; // do NOT TrimEnd now (we handled trailing space above)
        var lastSpace = trimmed.LastIndexOf(' ');
        return lastSpace >= 0 ? trimmed[(lastSpace + 1)..] : trimmed;
    }
}