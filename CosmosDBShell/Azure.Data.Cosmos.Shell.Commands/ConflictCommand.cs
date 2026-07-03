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

[CosmosCommand("conflict", Aliases = ["conflictpolicy"])]
[CosmosExample("conflict show", Description = "Display the current container's conflict resolution policy")]
[CosmosExample("conflict set --mode lastWriterWins --path /_ts", Description = "Use last-writer-wins resolution based on the /_ts path")]
[CosmosExample("conflict set --mode custom --procedure resolveConflicts", Description = "Use a stored procedure to resolve conflicts")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Conflict",
    Description = @"
Views or changes the conflict resolution policy of a Cosmos DB container through subcommands:
- 'show' returns the current policy as JSON, including the mode ('LastWriterWins' or 'Custom'), the resolution path (last-writer-wins), and the resolution stored procedure (custom).
- 'set' updates the policy. Pass --mode to choose 'lastWriterWins' or 'custom'. For last-writer-wins pass --path to name the property that decides the winner (defaults to /_ts). For custom pass --procedure to name the stored procedure that resolves conflicts. Options that are not supplied keep their current value.

Conflict resolution policies only take effect on accounts configured for multi-region writes.

By default the command targets the current container. Use --database and --container to target a specific container.",
    ReadOnly = false)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class ConflictCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    [CosmosParameter("subcommand", RequiredErrorKey = "command-conflict-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosOption("mode", "m")]
    public string? Mode { get; init; }

    [CosmosOption("path", "p")]
    public string? Path { get; init; }

    [CosmosOption("procedure", "proc", "sproc")]
    public string? Procedure { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token) =>
        shell.State.AcceptAsync(this, shell, token);

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("conflict");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database) && !string.IsNullOrEmpty(this.Container))
        {
            return await this.ExecuteOnContainerAsync(state, this.Database, this.Container, token);
        }

        throw new NotInContainerException("conflict");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;

        if (!string.IsNullOrEmpty(this.Container))
        {
            return await this.ExecuteOnContainerAsync(state, databaseName, this.Container, token);
        }

        throw new NotInContainerException("conflict");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.ExecuteOnContainerAsync(state, databaseName, containerName, token);
    }

    /// <summary>
    /// Normalizes the value of the <c>--mode</c> option to a canonical conflict
    /// resolution mode. Returns null when no value was supplied. Accepts
    /// <c>lastWriterWins</c> (and the <c>lww</c> shorthand) or <c>custom</c>
    /// case-insensitively and rejects anything else.
    /// </summary>
    internal static string? NormalizeMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "lastwriterwins" or "lww" or "last-writer-wins" => "lastWriterWins",
            "custom" => "custom",
            _ => throw new CommandException("conflict", MessageService.GetString("command-conflict-error-invalid_mode")),
        };
    }

    private static bool IsCustom(string mode) => string.Equals(mode, "custom", StringComparison.OrdinalIgnoreCase);

    private static CommandState BuildResult(string containerName, ConflictResolutionView view)
    {
        var root = new JsonObject
        {
            ["container"] = containerName,
            ["mode"] = view.Mode,
            ["resolutionPath"] = view.ResolutionPath,
            ["resolutionProcedure"] = view.ResolutionProcedure,
        };

        using var jsonDoc = JsonDocument.Parse(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new CommandState
        {
            Result = new ShellJson(jsonDoc.RootElement.Clone()),
        };
    }

    private async Task<CommandState> ExecuteOnContainerAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        // Validate the subcommand and options that do not depend on the current policy
        // before touching the network so bad input fails fast without a round-trip.
        string? normalizedMode = null;
        bool isShow = false;
        switch (this.Subcommand.Trim().ToLowerInvariant())
        {
            case "show":
                isShow = true;
                break;
            case "set":
                normalizedMode = this.PreValidateSet();
                break;
            default:
                throw new CommandException(
                    "conflict",
                    MessageService.GetArgsString("command-conflict-error-invalid_subcommand", "subcommand", this.Subcommand));
        }

        await ValidateContainerExistsAsync(state, databaseName, containerName, "conflict", token);

        if (isShow)
        {
            var current = await CosmosResourceFacade.GetConflictResolutionPolicyAsync(state, databaseName, containerName, token);
            return BuildResult(containerName, current);
        }

        return await this.SetAsync(state, databaseName, containerName, normalizedMode, token);
    }

    /// <summary>
    /// Validates the options for 'conflict set' that do not depend on the container's
    /// current policy and returns the normalized mode (null when --mode was omitted).
    /// </summary>
    private string? PreValidateSet()
    {
        string? normalizedMode = NormalizeMode(this.Mode);
        if (normalizedMode is null && this.Path is null && this.Procedure is null)
        {
            throw new CommandException("conflict", MessageService.GetString("command-conflict-error-missing_set_args"));
        }

        if (string.Equals(normalizedMode, "custom", StringComparison.Ordinal) && this.Path is not null)
        {
            throw new CommandException("conflict", MessageService.GetString("command-conflict-error-path_with_custom"));
        }

        if (string.Equals(normalizedMode, "lastWriterWins", StringComparison.Ordinal) && this.Procedure is not null)
        {
            throw new CommandException("conflict", MessageService.GetString("command-conflict-error-procedure_with_lww"));
        }

        return normalizedMode;
    }

    private async Task<CommandState> SetAsync(ConnectedState state, string databaseName, string containerName, string? normalizedMode, CancellationToken token)
    {
        var current = await CosmosResourceFacade.GetConflictResolutionPolicyAsync(state, databaseName, containerName, token);
        string effectiveMode = normalizedMode ?? (IsCustom(current.Mode) ? "custom" : "lastWriterWins");

        ConflictResolutionUpdate update;
        if (IsCustom(effectiveMode))
        {
            if (this.Path is not null)
            {
                throw new CommandException("conflict", MessageService.GetString("command-conflict-error-path_with_custom"));
            }

            string? procedure = this.Procedure ?? current.ResolutionProcedure;
            if (string.IsNullOrWhiteSpace(procedure))
            {
                throw new CommandException("conflict", MessageService.GetString("command-conflict-error-missing_procedure"));
            }

            update = new ConflictResolutionUpdate("custom", null, procedure);
        }
        else
        {
            if (this.Procedure is not null)
            {
                throw new CommandException("conflict", MessageService.GetString("command-conflict-error-procedure_with_lww"));
            }

            string path = this.Path ?? current.ResolutionPath ?? "/_ts";
            update = new ConflictResolutionUpdate("lastWriterWins", path, null);
        }

        var view = await CosmosResourceFacade.ReplaceConflictResolutionPolicyAsync(state, databaseName, containerName, update, token);
        ShellInterpreter.WriteLine(MessageService.GetString("command-conflict-updated"));
        return BuildResult(containerName, view);
    }
}
