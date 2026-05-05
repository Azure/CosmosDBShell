//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using RadLine;

internal class MoveToEndOfLineCommand : LineEditorCommand
{
    public override void Execute(LineEditorContext context)
    {
        context.Buffer.MoveEnd();
    }
}
