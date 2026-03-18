//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Diagnostics;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;

[CosmosCommand("jq", External = true)]
[CosmosExample("echo '{\"a\":1}' | jq", Description = "Process JSON with jq")]
[CosmosExample("query \"SELECT * FROM c\" | jq '.items[0]'", Description = "Extract first item from query results")]
[CosmosExample("ls | jq 'length'", Description = "Count number of items in list")]
internal class JqCommand : CosmosCommand
{
    [CosmosParameter("args", IsRequired = false)]
    public string[]? Arguments { get; init; }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var psi = new ProcessStartInfo("jq", this.Arguments ?? [])
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = !string.IsNullOrEmpty(shell.StdOutRedirect),
        };

        using var p = Process.Start(psi) ?? throw new CommandException("jq", MessageService.GetString("error-start_process"));
        var sw = p.StandardInput;
        var doc = commandState?.Result?.ConvertShellObject(Parser.DataType.Text) as string ?? string.Empty;
        await sw.WriteLineAsync(doc);
        sw.Close();

        if (!string.IsNullOrEmpty(shell.StdOutRedirect))
        {
            shell.Redirect(await p.StandardOutput.ReadToEndAsync());
        }

        p.WaitForExit();

        return new CommandState();
    }
}
