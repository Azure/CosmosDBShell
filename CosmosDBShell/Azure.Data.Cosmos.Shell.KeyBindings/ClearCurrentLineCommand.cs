//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using RadLine;

internal class ClearCurrentLineCommand : LineEditorCommand
{
    public override void Execute(LineEditorContext context)
    {
        context.Buffer.Clear(0, context.Buffer.Length);
        context.Buffer.MoveEnd();
    }
}
