//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("throughput")]
[CosmosExample("throughput show", Description = "Display the current provisioned throughput (RU/s)")]
[CosmosExample("throughput set 4000", Description = "Set manual throughput to 4000 RU/s")]
[CosmosExample("throughput manual 4000", Description = "Switch to manual provisioning at 4000 RU/s")]
[CosmosExample("throughput autoscale 10000", Description = "Switch to autoscale with a maximum of 10000 RU/s")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Throughput",
    Description = @"
Views or changes the provisioned throughput (RU/s) of a Cosmos DB database or container through subcommands:
- 'show' returns the current throughput as JSON, including the mode (manual or autoscale), provisioned RU/s, autoscale maximum, and minimum.
- 'set <RUs>' sets manual throughput to the given RU/s (alias of 'manual').
- 'manual <RUs>' switches to manual provisioning at the given RU/s.
- 'autoscale <maxRUs>' switches to autoscale with the given maximum RU/s.

By default the command targets the current scope: the container when in a container, otherwise the database. Use --database and --container to target a specific resource.",
    ReadOnly = false)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class ThroughputCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    [CosmosParameter("subcommand", RequiredErrorKey = "command-throughput-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("ru", IsRequired = false)]
    public int? Ru { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token) =>
        shell.State.AcceptAsync(this, shell, token);

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("throughput");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        if (string.IsNullOrEmpty(this.Database))
        {
            throw new NotInDatabaseException("throughput");
        }

        return await this.ExecuteOnScopeAsync(state, this.Database, this.Container, token);
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        return await this.ExecuteOnScopeAsync(state, databaseName, this.Container, token);
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;
        return await this.ExecuteOnScopeAsync(state, databaseName, containerName, token);
    }

    private static CommandState BuildResult(ThroughputView view)
    {
        string mode = view.Availability == ThroughputAvailability.NotConfigured
            ? "none"
            : view.IsAutoscale ? "autoscale" : "manual";

        var root = new JsonObject
        {
            ["scope"] = view.Scope,
            ["resource"] = view.ResourceName,
            ["mode"] = mode,
            ["throughput"] = view.Throughput,
            ["autoscaleMaxThroughput"] = view.AutoscaleMaxThroughput,
            ["minThroughput"] = view.MinThroughput,
        };

        using var jsonDoc = JsonDocument.Parse(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new CommandState
        {
            Result = new ShellJson(jsonDoc.RootElement.Clone()),
        };
    }

    private async Task<CommandState> ExecuteOnScopeAsync(ConnectedState state, string databaseName, string? containerName, CancellationToken token)
    {
        bool isWrite;
        bool isAutoscale;
        switch (this.Subcommand.Trim().ToLowerInvariant())
        {
            case "show":
                isWrite = false;
                isAutoscale = false;
                break;
            case "set":
            case "manual":
                isWrite = true;
                isAutoscale = false;
                break;
            case "autoscale":
                isWrite = true;
                isAutoscale = true;
                break;
            default:
                throw new CommandException(
                    "throughput",
                    MessageService.GetArgsString("command-throughput-error-invalid_subcommand", "subcommand", this.Subcommand));
        }

        int ru = 0;
        if (isWrite)
        {
            ru = this.RequireRu();
        }
        else if (this.Ru.HasValue)
        {
            throw new CommandException("throughput", MessageService.GetString("command-throughput-error-show_no_args"));
        }

        await ValidateContainerExistsAsync(state, databaseName, containerName, "throughput", token);

        if (!isWrite)
        {
            var current = await CosmosResourceFacade.GetThroughputAsync(state, databaseName, containerName, token);
            return BuildResult(current);
        }

        ThroughputView view;
        try
        {
            view = await CosmosResourceFacade.ReplaceThroughputAsync(state, databaseName, containerName, new ThroughputUpdate(isAutoscale, ru), token);
        }
        catch (ThroughputNotConfiguredException ex)
        {
            throw new CommandException(
                "throughput",
                MessageService.GetArgsString("command-throughput-error-not_configured", "resource", ex.ResourceName),
                ex);
        }

        ShellInterpreter.WriteLine(MessageService.GetString("command-throughput-updated"));
        return BuildResult(view);
    }

    private int RequireRu()
    {
        if (!this.Ru.HasValue)
        {
            throw new CommandException("throughput", MessageService.GetString("command-throughput-error-missing_ru"));
        }

        if (this.Ru.Value <= 0)
        {
            throw new CommandException(
                "throughput",
                MessageService.GetArgsString("command-throughput-error-invalid_ru", "ru", this.Ru.Value));
        }

        return this.Ru.Value;
    }
}
