// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using RadLine;

using Spectre.Console;

internal class CosmosCompletionRenderer(ShellInterpreter shellInterpreter) : ILineDecorationRenderer
{
    private readonly ShellInterpreter shellInterpreter = shellInterpreter;

    void ILineDecorationRenderer.RenderLineDecoration(LineBuffer buffer)
    {
        var word = buffer.Content;
        var completion = CosmosCompleteCommand.GetCompletion(this.shellInterpreter, word, AutoComplete.Next);

        if (!string.IsNullOrEmpty(completion) && word.Length < completion.Length)
        {
            completion = completion[word.Length..];
            AnsiConsole.Markup("[grey30]" + completion + "[/]");
            AnsiConsole.Cursor.Move(CursorDirection.Left, completion.Length);
        }
    }
}
