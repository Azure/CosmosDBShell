//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

[CosmosCommand("throughput")]
[CosmosExample("throughput show", Description = "Display the current provisioned throughput (RU/s)")]
[CosmosExample("throughput set 4000", Description = "Set manual throughput to 4000 RU/s")]
[CosmosExample("throughput manual 4000", Description = "Switch to manual provisioning at 4000 RU/s")]
[CosmosExample("throughput autoscale 10000", Description = "Switch to autoscale with a maximum of 10000 RU/s")]
[CosmosExample("throughput set 4000 --yes", Description = "Set throughput without the confirmation prompt")]
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
    // Matches the Cosmos DB RBAC "action denied" message returned for both data-plane
    // (CosmosException 403/5302) and control-plane throughput writes. The principal id
    // can be padded with whitespace inside the brackets.
    private static readonly Regex RbacPermissionRegex = new(
        @"principal \[\s*([^\]]+?)\s*\] does not have required RBAC permissions to perform action \[([^\]]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [CosmosParameter("subcommand", RequiredErrorKey = "command-throughput-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("ru", IsRequired = false)]
    public int? Ru { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("yes", "y", "force")]
    public bool? Yes { get; init; }

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

        return await this.ExecuteOnScopeAsync(state, shell, this.Database, this.Container, token);
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        return await this.ExecuteOnScopeAsync(state, shell, databaseName, this.Container, token);
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;
        return await this.ExecuteOnScopeAsync(state, shell, databaseName, containerName, token);
    }

    private static CommandState BuildResult(ShellInterpreter shell, ThroughputView view)
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
        var result = new ShellJson(jsonDoc.RootElement.Clone());

        // When output is redirected to a file, let the interpreter emit the JSON result
        // so 'throughput show > out.json' honors the documented JSON contract instead of
        // writing a console table. Interactive sessions still get the friendly table, and
        // MCP/piping consume the structured Result regardless.
        if (!string.IsNullOrEmpty(shell.StdOutRedirect))
        {
            return new CommandState { Result = result };
        }

        var table = new Table().HideHeaders();
        table.AddColumn(string.Empty);
        table.AddColumn(string.Empty);
        void Row(string labelKey, string value) =>
            table.AddRow(
                Theme.FormatHelpName(Markup.Escape(MessageService.GetString(labelKey))),
                Theme.FormatTableValue(Markup.Escape(value)));

        Row("command-throughput-label-scope", MessageService.GetString($"command-throughput-scope-{view.Scope}"));
        Row("command-throughput-label-resource", view.ResourceName);
        Row("command-throughput-label-mode", MessageService.GetString($"command-throughput-mode-{mode}"));
        if (view.Throughput.HasValue)
        {
            Row("command-throughput-label-throughput", view.Throughput.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (view.AutoscaleMaxThroughput.HasValue)
        {
            Row("command-throughput-label-max", view.AutoscaleMaxThroughput.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (view.MinThroughput.HasValue)
        {
            Row("command-throughput-label-min", view.MinThroughput.Value.ToString(CultureInfo.InvariantCulture));
        }

        AnsiConsole.Write(table);

        return new CommandState
        {
            Result = result,
            IsPrinted = true,
        };
    }

    private static bool TryGetRbacError(Exception e, out string principalId, out string action)
    {
        var match = RbacPermissionRegex.Match(e.Message ?? string.Empty);
        if (match.Success)
        {
            principalId = match.Groups[1].Value.Trim();
            action = match.Groups[2].Value.Trim();
            return true;
        }

        principalId = string.Empty;
        action = string.Empty;
        return false;
    }

    // Returns true when the throughput write should proceed. Interactive sessions get a
    // billing-impact confirmation prompt; --yes/--force, MCP, script, and piped-input
    // contexts skip it so automation never blocks on Console input.
    private static bool ConfirmWrite(ShellInterpreter shell, bool yes, string databaseName, string? containerName, bool isAutoscale, int ru)
    {
        if (yes || shell.McpPort.HasValue || !string.IsNullOrEmpty(shell.CurrentScriptFileName) || Console.IsInputRedirected)
        {
            return true;
        }

        string resourceName = containerName ?? databaseName;
        string modeLabel = MessageService.GetString(isAutoscale ? "command-throughput-mode-autoscale" : "command-throughput-mode-manual");
        string ruText = ru.ToString(CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine(MessageService.GetArgsString("command-throughput-confirm_summary", "resource", Markup.Escape(resourceName), "mode", modeLabel, "ru", ruText));
        return ShellInterpreter.Confirm("command-throughput-confirm");
    }

    private async Task<CommandState> ExecuteOnScopeAsync(ConnectedState state, ShellInterpreter shell, string databaseName, string? containerName, CancellationToken token)
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
            ru = this.RequireRu(isAutoscale);
        }
        else if (this.Ru.HasValue)
        {
            throw new CommandException("throughput", MessageService.GetString("command-throughput-error-show_no_args"));
        }

        await ValidateContainerExistsAsync(state, databaseName, containerName, "throughput", token);

        if (!isWrite)
        {
            var current = await CosmosResourceFacade.GetThroughputAsync(state, databaseName, containerName, token);
            return BuildResult(shell, current);
        }

        if (!ConfirmWrite(shell, this.Yes == true, databaseName, containerName, isAutoscale, ru))
        {
            ShellInterpreter.WriteLine(MessageService.GetString("command-throughput-cancelled"));
            return new CommandState { IsPrinted = true };
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
        catch (ThroughputModeSwitchNotSupportedException ex)
        {
            string targetMode = MessageService.GetString(ex.TargetIsAutoscale ? "command-throughput-mode-autoscale" : "command-throughput-mode-manual");
            throw new CommandException(
                "throughput",
                MessageService.GetArgsString("command-throughput-error-mode_switch_unsupported", "resource", ex.ResourceName, "mode", targetMode),
                ex);
        }
        catch (Exception ex) when (TryGetRbacError(ex, out var principalId, out var action))
        {
            throw new CommandException(
                "throughput",
                MessageService.GetArgsString("command-throughput-error-rbac", "id", principalId, "permission", action),
                ex);
        }

        ShellInterpreter.WriteLine(MessageService.GetString("command-throughput-updated"));
        return BuildResult(shell, view);
    }

    private int RequireRu(bool isAutoscale)
    {
        if (!this.Ru.HasValue)
        {
            throw new CommandException("throughput", MessageService.GetString("command-throughput-error-missing_ru"));
        }

        int value = this.Ru.Value;
        if (value <= 0)
        {
            throw new CommandException(
                "throughput",
                MessageService.GetArgsString("command-throughput-error-invalid_ru", "ru", value));
        }

        // Cosmos DB requires manual RU/s in multiples of 100 (minimum 400) and autoscale
        // maximum RU/s in multiples of 1000 (minimum 1000). Validate up front so the user
        // gets a clean message instead of a raw server rejection.
        int minimum = isAutoscale ? 1000 : 400;
        int increment = isAutoscale ? 1000 : 100;
        if (value < minimum)
        {
            string key = isAutoscale ? "command-throughput-error-autoscale_min" : "command-throughput-error-manual_min";
            throw new CommandException(
                "throughput",
                MessageService.GetArgsString(key, "ru", value, "min", minimum));
        }

        if (value % increment != 0)
        {
            string key = isAutoscale ? "command-throughput-error-autoscale_increment" : "command-throughput-error-manual_increment";
            throw new CommandException(
                "throughput",
                MessageService.GetArgsString(key, "ru", value, "increment", increment));
        }

        return value;
    }
}
