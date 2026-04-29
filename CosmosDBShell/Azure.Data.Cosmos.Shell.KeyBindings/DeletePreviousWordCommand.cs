//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using RadLine;

internal class DeletePreviousWordCommand : LineEditorCommand
{
    public override void Execute(LineEditorContext context)
    {
        var end = context.Buffer.Position;
        if (end <= 0)
        {
            return;
        }

        var content = context.Buffer.Content;
        var start = end;

        while (start > 0 && char.IsWhiteSpace(content[start - 1]))
        {
            start--;
        }

        while (start > 0 && !char.IsWhiteSpace(content[start - 1]))
        {
            start--;
        }

        var length = end - start;
        if (length <= 0)
        {
            return;
        }

        context.Buffer.Clear(start, length);
        context.Buffer.Move(start);
    }
}
