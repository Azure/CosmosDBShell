//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using RadLine;
using Spectre.Console;

internal class ClearScreenCommand : LineEditorCommand
{
    public override void Execute(LineEditorContext context)
    {
        AnsiConsole.Clear();
    }
}
