//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

[CosmosCommand("rmdb")]
[CosmosExample("rmdb TestDatabase", Description = "Delete database with confirmation prompt")]
[CosmosExample("rmdb OldDB true", Description = "Delete database without confirmation")]
[McpAnnotation(Title = "Remove DataBase", Restricted = true, Destructive = true)]
internal class RmDbCommand : CosmosCommand, IStateVisitor<ExitCode, ShellInterpreter>
{
    [CosmosParameter("name", ParameterType = ParameterType.Database)]
    public string? Name { get; init; }

    [CosmosParameter("force", IsRequired = false, ParameterType = ParameterType.Database)]
    public bool? Force { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        await shell.State.AcceptAsync(this, shell, token);
        return commandState;
    }

    Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter data, CancellationToken token)
    {
        throw new NotConnectedException("rmdb");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        return await this.RemoveDatabaseAsync(state.Client, shell, token);
    }

    async Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        return await this.RemoveDatabaseAsync(state.Client, shell, token);
    }

    Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new CommandException("rmdb", MessageService.GetString("command-rmdb-error-not_allowed_in_container"));
    }

    internal static void UpdateStateAfterDelete(ShellInterpreter shell, CosmosClient client, string deletedDatabaseName)
    {
        if (shell.State is DatabaseState databaseState && databaseState.DatabaseName == deletedDatabaseName)
        {
            var connectedState = new ConnectedState(client);
            shell.State = connectedState;

            CosmosCompleteCommand.ClearContainers();
        }
    }

    private async Task<ExitCode> RemoveDatabaseAsync(CosmosClient client, ShellInterpreter shell, CancellationToken token)
    {
        await foreach (var database in EnumerateDatabasesAsync(client))
        {
            if (token.IsCancellationRequested)
            {
                return -1;
            }

            if (database.Id == this.Name)
            {
                    var db = client.GetDatabase(this.Name);
                    if (this.Force is true || ShellInterpreter.Confirm("command-rmdb-confirm_db_deletion"))
                {
                    await db.DeleteAsync(cancellationToken: token);
                    UpdateStateAfterDelete(shell, client, database.Id);
                    CosmosCompleteCommand.ClearDatabases();
                        AnsiConsole.MarkupLine(MessageService.GetString("command-rmdb-deleted_db", new Dictionary<string, object> { { "db", database.Id } }));
                }

                return 0;
            }
        }

        throw new CommandException("rmdb", MessageService.GetString("command-rmdb-error-database_not_found", new Dictionary<string, object> { { "db", this.Name ?? string.Empty } }));
    }
}
