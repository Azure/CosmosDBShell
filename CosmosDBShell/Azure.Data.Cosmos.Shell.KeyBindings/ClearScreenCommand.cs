//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using Azure.Data.Cosmos.Shell.Core;
using RadLine;

internal class ClearScreenCommand : LineEditorCommand
{
    public override void Execute(LineEditorContext context)
    {
        ShellInterpreter.Instance.Output.ClearScreen();
    }
}
