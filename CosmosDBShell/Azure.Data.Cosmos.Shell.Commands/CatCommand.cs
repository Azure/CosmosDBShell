//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

[CosmosCommand("cat")]
[CosmosExample("cat config.json", Description = "Display contents of a JSON configuration file")]
[CosmosExample("cat data.txt", Description = "Display contents of a text file")]
internal class CatCommand : CosmosCommand, IStateVisitor<int, string>
{
    [CosmosParameter("path", IsRequired = false, ParameterType = ParameterType.File)]
    public string? FilePath { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (!File.Exists(this.FilePath))
        {
            throw new CommandException("cat", MessageService.GetString("error-file_not_found", new Dictionary<string, object> { { "file", this.FilePath ?? string.Empty } }));
        }

        commandState.Result = new ShellText(File.ReadAllText(this.FilePath));
        return Task.FromResult(commandState);
    }

    public Task<int> VisitConnectedStateAsync(ConnectedState state, string data, CancellationToken token)
    {
        return Task.FromResult(0);
    }

    public Task<int> VisitContainerStateAsync(ContainerState state, string data, CancellationToken token)
    {
        return Task.FromResult(0);
    }

    public Task<int> VisitDatabaseStateAsync(DatabaseState state, string data, CancellationToken token)
    {
        return Task.FromResult(0);
    }

    public Task<int> VisitDisconnectedStateAsync(DisconnectedState state, string data, CancellationToken token)
    {
        return Task.FromResult(0);
    }
}
