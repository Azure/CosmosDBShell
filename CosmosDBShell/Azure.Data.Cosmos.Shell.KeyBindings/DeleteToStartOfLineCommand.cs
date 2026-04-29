//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using RadLine;

internal class DeleteToStartOfLineCommand : LineEditorCommand
{
    public override void Execute(LineEditorContext context)
    {
        var position = context.Buffer.Position;
        if (position <= 0)
        {
            return;
        }

        context.Buffer.Clear(0, position);
        context.Buffer.Move(0);
    }
}
