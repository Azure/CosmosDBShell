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
        await foreach (var database in EnumerateDatabasesAsync(state.Client))
        {
            if (token.IsCancellationRequested)
            {
                return -1;
            }

            if (database.Id == this.Name)
            {
                var db = state.Client.GetDatabase(this.Name);
                if (ShellInterpreter.Confirm("command-rmcon-confirm_container_deletion") || this.Force == true)
                {
                    await db.DeleteAsync(cancellationToken: token);
                    CosmosCompleteCommand.ClearDatabases();
                    AnsiConsole.MarkupLine(MessageService.GetString("command-rmdb-deleted_database", new Dictionary<string, object> { { "db", this.Name } }));
                }

                return 0;
            }
        }

        throw new CommandException("rmdb", MessageService.GetString("command-rmdb-error-database_not_found", new Dictionary<string, object> { { "db", this.Name ?? string.Empty } }));
    }

    async Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        await foreach (var containerProperty in EnumerateContainersAsync(state.Client.GetDatabase(state.DatabaseName)))
        {
            if (containerProperty.Id == this.Name)
            {
                var c = state.Client.GetContainer(state.DatabaseName, this.Name);
                if (ShellInterpreter.Confirm("command-rmcon-confirm_container_deletion") || this.Force == true)
                {
                    await c.DeleteContainerAsync(cancellationToken: token);
                    AnsiConsole.MarkupLine(MessageService.GetString("command-rmdb-deleted_db", new Dictionary<string, object> { { "db", this.Name } }));
                }

                return 0;
            }
        }

        throw new CommandException("rmdb", MessageService.GetString("command-rmdb-error-database_not_found", new Dictionary<string, object> { { "db", this.Name ?? string.Empty } }));
    }

    Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new CommandException("rmdb", MessageService.GetString("command-rmdb-error-not_allowed_in_container"));
    }
}
