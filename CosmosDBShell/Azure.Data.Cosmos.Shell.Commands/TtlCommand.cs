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

[CosmosCommand("ttl")]
[CosmosExample("ttl show", Description = "Display the current container's default time-to-live configuration")]
[CosmosExample("ttl set 86400", Description = "Expire items 86400 seconds (1 day) after they were last modified")]
[CosmosExample("ttl on", Description = "Enable TTL with no container default, so only items with their own 'ttl' expire")]
[CosmosExample("ttl off", Description = "Disable TTL so items never expire")]
[CosmosExample("ttl show --analytical", Description = "Display the current container's analytical store time-to-live configuration")]
[CosmosExample("ttl set 2592000 --analytical", Description = "Retain analytical store data for 2592000 seconds (30 days)")]
[CosmosExample("ttl on --analytical", Description = "Enable the analytical store with indefinite retention")]
[CosmosExample("ttl off --analytical", Description = "Disable the analytical store")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Ttl",
    Description = @"
Views or changes the time-to-live (TTL) of a Cosmos DB container through subcommands:
- 'show' returns the current TTL configuration as JSON. 'status' is one of 'disabled' (items never expire), 'no-default' (TTL is on but items expire only when they carry their own 'ttl' property), or 'enabled' (items expire after 'defaultTimeToLiveSeconds').
- 'set <seconds>' enables TTL with a positive default expiration in seconds.
- 'on' enables TTL with no container default (equivalent to a default TTL of -1); only items with their own 'ttl' property expire.
- 'off' disables TTL so items never expire.

Pass --analytical to target the container's analytical store TTL instead of the default item TTL. With --analytical, 'set <seconds>' retains analytical data for that many seconds, 'on' enables the analytical store with indefinite retention (a TTL of -1), 'off' disables the analytical store, and 'show' reports the analytical status ('disabled' or 'enabled') and 'analyticalTimeToLiveSeconds'. The analytical store must be supported by the account.

By default the command targets the current container. Use --database and --container to target a specific container.",
    ReadOnly = false)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class TtlCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    [CosmosParameter("subcommand", RequiredErrorKey = "command-ttl-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("seconds", IsRequired = false)]
    public int? Seconds { get; init; }

    [CosmosOption("analytical", "a")]
    public bool Analytical { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token) =>
        shell.State.AcceptAsync(this, shell, token);

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("ttl");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(this.Database) && !string.IsNullOrEmpty(this.Container))
        {
            return await this.ExecuteOnContainerAsync(state, this.Database, this.Container, token);
        }

        throw new NotInContainerException("ttl");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;

        if (!string.IsNullOrEmpty(this.Container))
        {
            return await this.ExecuteOnContainerAsync(state, databaseName, this.Container, token);
        }

        throw new NotInContainerException("ttl");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.ExecuteOnContainerAsync(state, databaseName, containerName, token);
    }

    /// <summary>
    /// Maps a raw Cosmos DB default TTL value to a friendly status string. <c>null</c>
    /// means TTL is disabled, <c>-1</c> means TTL is enabled with no container default,
    /// and any positive value means items expire after that many seconds.
    /// </summary>
    internal static string StatusFor(int? defaultTimeToLive) => defaultTimeToLive switch
    {
        null => "disabled",
        -1 => "no-default",
        _ => "enabled",
    };

    /// <summary>
    /// Maps a raw Cosmos DB analytical store TTL value to a friendly status string.
    /// <c>null</c> or <c>0</c> means the analytical store is disabled; any other value
    /// (<c>-1</c> for indefinite retention or a positive number of seconds) means it is
    /// enabled.
    /// </summary>
    internal static string AnalyticalStatusFor(long? analyticalTimeToLive) => analyticalTimeToLive switch
    {
        null or 0 => "disabled",
        _ => "enabled",
    };

    private static CommandState BuildResult(string containerName, ContainerTtlView view)
    {
        var root = new JsonObject
        {
            ["container"] = containerName,
            ["status"] = StatusFor(view.DefaultTimeToLive),
            ["defaultTimeToLiveSeconds"] = view.DefaultTimeToLive,
        };

        using var jsonDoc = JsonDocument.Parse(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new CommandState
        {
            Result = new ShellJson(jsonDoc.RootElement.Clone()),
        };
    }

    private static CommandState BuildAnalyticalResult(string containerName, ContainerAnalyticalTtlView view)
    {
        var root = new JsonObject
        {
            ["container"] = containerName,
            ["scope"] = "analytical",
            ["status"] = AnalyticalStatusFor(view.AnalyticalTimeToLiveSeconds),
            ["analyticalTimeToLiveSeconds"] = view.AnalyticalTimeToLiveSeconds,
        };

        using var jsonDoc = JsonDocument.Parse(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new CommandState
        {
            Result = new ShellJson(jsonDoc.RootElement.Clone()),
        };
    }

    private async Task<CommandState> ExecuteOnContainerAsync(ConnectedState state, string databaseName, string containerName, CancellationToken token)
    {
        // Validate the subcommand and its arguments before touching the network so bad
        // input fails fast without a round-trip.
        bool isShow = false;
        int? targetTtl = null;
        switch (this.Subcommand.Trim().ToLowerInvariant())
        {
            case "show":
                isShow = true;
                if (this.Seconds.HasValue)
                {
                    throw new CommandException("ttl", MessageService.GetString("command-ttl-error-show_no_args"));
                }

                break;
            case "set":
                targetTtl = this.RequireSeconds();
                break;
            case "on":
                this.RejectSeconds();
                targetTtl = -1;
                break;
            case "off":
                this.RejectSeconds();
                targetTtl = null;
                break;
            default:
                throw new CommandException(
                    "ttl",
                    MessageService.GetArgsString("command-ttl-error-invalid_subcommand", "subcommand", this.Subcommand));
        }

        await ValidateContainerExistsAsync(state, databaseName, containerName, "ttl", token);

        if (this.Analytical)
        {
            if (isShow)
            {
                var currentAnalytical = await CosmosResourceFacade.GetAnalyticalTimeToLiveAsync(state, databaseName, containerName, token);
                return BuildAnalyticalResult(containerName, currentAnalytical);
            }

            var analyticalView = await CosmosResourceFacade.ReplaceAnalyticalTimeToLiveAsync(state, databaseName, containerName, targetTtl, token);
            ShellInterpreter.WriteLine(MessageService.GetString("command-ttl-analytical-updated"));
            return BuildAnalyticalResult(containerName, analyticalView);
        }

        if (isShow)
        {
            var current = await CosmosResourceFacade.GetTimeToLiveAsync(state, databaseName, containerName, token);
            return BuildResult(containerName, current);
        }

        var view = await CosmosResourceFacade.ReplaceTimeToLiveAsync(state, databaseName, containerName, targetTtl, token);
        ShellInterpreter.WriteLine(MessageService.GetString("command-ttl-updated"));
        return BuildResult(containerName, view);
    }

    private int RequireSeconds()
    {
        if (!this.Seconds.HasValue)
        {
            throw new CommandException("ttl", MessageService.GetString("command-ttl-error-missing_seconds"));
        }

        int seconds = this.Seconds.Value;
        if (seconds <= 0)
        {
            throw new CommandException(
                "ttl",
                MessageService.GetArgsString("command-ttl-error-invalid_seconds", "seconds", seconds));
        }

        return seconds;
    }

    private void RejectSeconds()
    {
        if (this.Seconds.HasValue)
        {
            throw new CommandException("ttl", MessageService.GetString("command-ttl-error-toggle_no_args"));
        }
    }
}
