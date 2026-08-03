//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

[CosmosCommand("rmcon")]
[CosmosExample("rmcon OldContainer", Description = "Delete container with confirmation prompt")]
[CosmosExample("rmcon TempData true", Description = "Delete container skipping the interactive confirmation prompt (over MCP, confirmation is still required)")]
[CosmosExample("rmcon TestContainer --database=TestDB", Description = "Delete container from specific database")]
[CosmosExample("rmcon OldContainer --dry-run", Description = "Preview the deletion without deleting the container")]
[McpAnnotation(Title = "Remove Container", Restricted = true, Destructive = true, Confirmable = true)]
internal class RmContainerCommand : CosmosCommand, IStateVisitor<ExitCode, ShellInterpreter>
{
    [CosmosParameter("name", ParameterType = ParameterType.Container)]
    public string? Name { get; init; }

    [CosmosParameter("force", IsRequired = false, ParameterType = ParameterType.Database)]
    public bool? Force { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("dry-run")]
    public bool? DryRun { get; init; }

    internal CommandState? Outcome { get; set; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        this.Outcome = commandState;
        await shell.State.AcceptAsync(this, shell, token);
        return commandState;
    }

    Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter data, CancellationToken token)
    {
        throw new NotConnectedException("rmcon");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database))
        {
            return await this.RemoveContainerAsync(state, this.Database, token);
        }

        throw new NotInDatabaseException("rmcon");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database))
        {
            return await this.RemoveContainerAsync(state, this.Database, token);
        }

        return await this.RemoveContainerAsync(state, state.DatabaseName, token);
    }

    async Task<ExitCode> IStateVisitor<ExitCode, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database))
        {
            return await this.RemoveContainerAsync(state, this.Database, token);
        }

        throw new NotInContainerException("rmcon");
    }

    private async Task<ExitCode> RemoveContainerAsync(ConnectedState state, string databaseName, CancellationToken token)
    {
        // Validate database exists
        await ValidateDatabaseExistsAsync(state, databaseName, "rmcon", token);

        await foreach (var containerName in EnumerateContainerNamesAsync(state, databaseName, "rmcon", token))
        {
            if (token.IsCancellationRequested)
            {
                return -1;
            }

            if (containerName == this.Name)
            {
                if (this.DryRun == true)
                {
                    this.SetOutcome(
                        JsonSerializer.SerializeToElement(new { container = containerName, dryRun = true, deleted = false }),
                        () => AnsiConsole.MarkupLine(MessageService.GetString("command-rmcon-dry-run-plan", new Dictionary<string, object> { { "container", Markup.Escape(containerName) } })));
                    return 0;
                }

                if (this.Force == true || ShellInterpreter.Confirm("command-rmcon-confirm_container_deletion"))
                {
                    await CosmosResourceFacade.DeleteContainerAsync(state, databaseName, containerName, token);
                    CosmosCompleteCommand.ClearContainers();
                    this.SetOutcome(
                        JsonSerializer.SerializeToElement(new { container = containerName, deleted = true }),
                        () => AnsiConsole.MarkupLine(MessageService.GetString("command-rmcon-deleted_container", new Dictionary<string, object> { { "container", Markup.Escape(containerName) } })));
                }
                else
                {
                    this.SetOutcome(
                        JsonSerializer.SerializeToElement(new { container = containerName, deleted = false }),
                        () => { });
                }

                return 0;
            }
        }

        throw new CommandException("rmcon", MessageService.GetString("command-rmcon-error-container_not_found", new Dictionary<string, object> { { "container", this.Name ?? string.Empty } }));
    }

    private void SetOutcome(JsonElement payload, Action render)
    {
        var outcome = this.Outcome;
        if (outcome is null)
        {
            return;
        }

        outcome.Result = new ShellJson(payload);
        outcome.RenderUser = render;
    }
}
