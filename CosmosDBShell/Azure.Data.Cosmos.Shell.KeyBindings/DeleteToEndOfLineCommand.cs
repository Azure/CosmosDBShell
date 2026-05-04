//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using RadLine;

internal class DeleteToEndOfLineCommand : LineEditorCommand
{
    public override void Execute(LineEditorContext context)
    {
        var position = context.Buffer.Position;
        var length = context.Buffer.Length - position;
        if (length <= 0)
        {
            return;
        }

        context.Buffer.Clear(position, length);
    }
}
