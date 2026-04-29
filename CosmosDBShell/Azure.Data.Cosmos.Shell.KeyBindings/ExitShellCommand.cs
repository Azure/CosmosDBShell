//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

using Azure.Data.Cosmos.Shell.Core;
using RadLine;

internal class ExitShellCommand(ShellInterpreter shell) : LineEditorCommand
{
    private readonly ShellInterpreter shell = shell;

    public override void Execute(LineEditorContext context)
    {
        if (context.Buffer.Length == 0)
        {
            this.shell.IsRunning = false;
            context.Submit(SubmitAction.Cancel);
            return;
        }

        var position = context.Buffer.Position;
        if (position < context.Buffer.Length)
        {
            context.Buffer.Clear(position, 1);
        }
    }
}
