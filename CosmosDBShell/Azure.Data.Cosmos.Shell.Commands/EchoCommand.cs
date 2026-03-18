//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Spectre.Console;

[CosmosCommand("echo")]
[CosmosExample("echo \"Hello World\"", Description = "Print a simple text message")]
[CosmosExample("echo \"Hello\" \"World\"", Description = "Print multiple arguments concatenated with spaces")]
[CosmosExample("echo '{\"id\":1,\"name\":\"test\"}'", Description = "Output JSON data")]
[CosmosExample("echo \"test\" | jq", Description = "Pipe text output to jq for processing")]
internal class EchoCommand : CosmosCommand
{
    [CosmosParameter("messages", IsRequired = false)]
    public string[]? Messages { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var parts = new List<string>();

        // Include existing command state result if present
        if (commandState.Result != null && this.Messages == null)
        {
            var existingOutput = commandState.Result.ConvertShellObject(DataType.Text) as string;
            if (!string.IsNullOrEmpty(existingOutput))
            {
                parts.Add(existingOutput);
            }
        }

        // Include messages if provided
        if (this.Messages != null && this.Messages.Length > 0)
        {
            parts.Add(string.Join(" ", this.Messages));
        }

        var output = string.Join(" ", parts);
        commandState.Result = new ShellText(output);
        return Task.FromResult(commandState);
    }
}
