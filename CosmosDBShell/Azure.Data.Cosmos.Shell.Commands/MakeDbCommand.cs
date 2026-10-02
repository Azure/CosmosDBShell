//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("mkdb")]
[CosmosExample("mkdb MyDatabase", DescriptionKey = "command-mkdb-example-1")]
[CosmosExample("mkdb TestDB -scale=auto -ru=4000", DescriptionKey = "command-mkdb-example-2")]
[CosmosExample("mkdb ProdDB -scale=manual -ru=1000", DescriptionKey = "command-mkdb-example-3")]
internal class MakeDbCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    [CosmosParameter("name")]
    public string? Name { get; init; }

    [CosmosOption("scale", "s")]
    public string? Scale { get; init; }

    [CosmosOption("ru")]
    public int? MaxRU { get; init; }

    public static ThroughputProperties CreateThroughputProperties(string? scale, int? maxru)
    {
        return CreationThroughput.CreateProperties(scale, maxru);
    }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        return await shell.State.AcceptAsync(this, shell, token);
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("mkdb");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName;
        try
        {
            databaseName = await CosmosResourceFacade.CreateDatabaseAsync(state, this.Name ?? string.Empty, this.Scale, this.MaxRU, token);
        }
        catch (ServerlessThroughputNotSupportedException ex)
        {
            throw new CommandException("mkdb", MessageService.GetString("error-serverless_throughput_not_supported"), ex);
        }

        CosmosCompleteCommand.ClearDatabases();

        var commandState = new CommandState();
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            type = "database",
            id = databaseName,
            created = true,
        }));
        commandState.RenderUser = () => ShellInterpreter.WriteLine(MessageService.GetString("command-mkdb-database_created", new Dictionary<string, object> { { "db", databaseName } }));
        return commandState;
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new CommandException("mkdb", MessageService.GetString("error-not_allowed_in_db"));
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new CommandException("mkdb", MessageService.GetString("error-not_allowed_in_container"));
    }
}
