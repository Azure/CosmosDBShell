//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using RadLine;

internal class MoveCursorLeftCommand : LineEditorCommand
{
    public override void Execute(LineEditorContext context)
    {
        context.Buffer.MoveLeft(1);
    }
}
