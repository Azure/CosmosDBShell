//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

using Spectre.Console;

[CosmosCommand("bucket")]
[CosmosExample("bucket", Description = "Display the current client-side throughput bucket selection")]
[CosmosExample("bucket 3", Description = "Tag this client's requests with throughput bucket 3")]
[CosmosExample("bucket 0", Description = "Clear the client-side throughput bucket selection")]
[CosmosExample("bucket show", Description = "Show the current container's throughput bucket limits")]
[CosmosExample("bucket set 3 50", Description = "Limit bucket 3 to 50% of the container's throughput")]
[CosmosExample("bucket clear 3", Description = "Remove the limit configured for bucket 3")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Bucket",
    Description = @"
Manages Cosmos DB throughput buckets for the current connection through subcommands.

Client-side selection (works on any connected database or container scope):
- 'bucket' shows the bucket this client tags its requests with.
- 'bucket <1-5>' tags this client's requests with the given bucket.
- 'bucket 0' clears the client-side selection.

Container limits (requires an Entra/Azure AD connection and a container scope):
- 'bucket show' lists the container's per-bucket maximum throughput percentages and returns them as JSON.
- 'bucket set <1-5> <1-100>' limits a bucket to a percentage of the container's provisioned throughput.
- 'bucket clear <1-5>' removes a bucket's limit.

Use --database and --container to target a specific container.",
    ReadOnly = false)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class BucketCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    private enum BucketActionKind
    {
        ClientShow,
        ClientSet,
        ServerShow,
        ServerSet,
        ServerClear,
    }

    [CosmosParameter("action", IsRequired = false)]
    public string? Action { get; init; }

    [CosmosParameter("id", IsRequired = false)]
    public int? Id { get; init; }

    [CosmosParameter("percent", IsRequired = false)]
    public int? Percent { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("yes", "y", "force")]
    public bool? Yes { get; init; }

    public static bool CheckBucket(int bucket)
    {
        var isValid = bucket >= 0 && bucket <= 5;
        if (!isValid)
        {
            AnsiConsole.MarkupLine(MessageService.GetString("error-invalid_bucket_value", new Dictionary<string, object> { { "bucket", bucket } }));
        }

        return isValid;
    }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token) =>
        shell.State.AcceptAsync(this, shell, token);

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("bucket");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        if (this.IsServerAction())
        {
            string database = NormalizeOption(this.Database) ?? throw new NotInDatabaseException("bucket");
            return await this.ExecuteServerAsync(state, shell, database, NormalizeOption(this.Container), token);
        }

        throw new NotInDatabaseException("bucket");
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        if (this.IsServerAction())
        {
            string database = NormalizeOption(this.Database) ?? state.DatabaseName;
            return await this.ExecuteServerAsync(state, shell, database, NormalizeOption(this.Container), token);
        }

        return this.RunClientCommand(state.Client);
    }

    async Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        if (this.IsServerAction())
        {
            string database = NormalizeOption(this.Database) ?? state.DatabaseName;
            string containerName = NormalizeOption(this.Container) ?? state.ContainerName;
            return await this.ExecuteServerAsync(state, shell, database, containerName, token);
        }

        return this.RunClientCommand(state.Client);
    }

    // Treat an explicitly empty option (e.g. --container "") as "not provided" so it
    // falls back to the current scope instead of flowing an empty name into the SDK.
    private static string? NormalizeOption(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    // Returns true when a bucket-limit write should proceed. Interactive sessions get a
    // billing-impact confirmation prompt; --yes/--force, MCP, script, and piped-input
    // contexts skip it so automation never blocks on Console input.
    private static bool ConfirmWrite(ShellInterpreter shell, bool yes, string summaryKey, params object[] args)
    {
        if (yes || shell.McpPort.HasValue || !string.IsNullOrEmpty(shell.CurrentScriptFileName) || Console.IsInputRedirected)
        {
            return true;
        }

        AnsiConsole.MarkupLine(MessageService.GetArgsString(summaryKey, args));
        return ShellInterpreter.Confirm("command-bucket-confirm");
    }

    private static CommandState BuildShowResult(ShellInterpreter shell, ConnectedState state, ThroughputBucketsView view)
    {
        var bucketsArray = new JsonArray();
        foreach (var bucket in view.Buckets)
        {
            bucketsArray.Add(new JsonObject
            {
                ["id"] = bucket.Id,
                ["maxThroughputPercentage"] = bucket.MaxThroughputPercentage,
            });
        }

        int? clientBucket = state.Client.ClientOptions.ThroughputBucket;
        var root = new JsonObject
        {
            ["resource"] = view.ResourceName,
            ["mode"] = view.IsAutoscale ? "autoscale" : "manual",
            ["throughput"] = view.Throughput,
            ["autoscaleMaxThroughput"] = view.AutoscaleMaxThroughput,
            ["clientBucket"] = clientBucket,
            ["buckets"] = bucketsArray,
        };

        using var jsonDoc = JsonDocument.Parse(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var result = new ShellJson(jsonDoc.RootElement.Clone());

        // When output is redirected to a file, let the interpreter emit the JSON result so
        // 'bucket show > out.json' honors the structured contract instead of writing a table.
        if (!string.IsNullOrEmpty(shell.StdOutRedirect))
        {
            return new CommandState { Result = result };
        }

        if (view.Buckets.Count == 0)
        {
            AnsiConsole.MarkupLine(MessageService.GetArgsString("command-bucket-no_limits", "resource", Markup.Escape(view.ResourceName)));
        }
        else
        {
            var table = new Table();
            table.AddColumn(Theme.FormatHelpName(Markup.Escape(MessageService.GetString("command-bucket-label-id"))));
            table.AddColumn(Theme.FormatHelpName(Markup.Escape(MessageService.GetString("command-bucket-label-percent"))));
            foreach (var bucket in view.Buckets)
            {
                table.AddRow(
                    Theme.FormatTableValue(bucket.Id.ToString(CultureInfo.InvariantCulture)),
                    Theme.FormatTableValue(bucket.MaxThroughputPercentage.ToString(CultureInfo.InvariantCulture) + "%"));
            }

            AnsiConsole.Write(table);
        }

        WriteClientSelection(clientBucket);
        return new CommandState { Result = result, IsPrinted = true };
    }

    private static void WriteClientSelection(int? clientBucket)
    {
        if (clientBucket.HasValue)
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-bucket-current", new Dictionary<string, object> { { "bucket", Theme.FormatTableValue(clientBucket.Value.ToString(CultureInfo.InvariantCulture)) } }));
        }
        else
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-bucket-no_bucket"));
        }
    }

    private BucketActionKind Classify()
    {
        var raw = this.Action?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return BucketActionKind.ClientShow;
        }

        switch (raw.ToLowerInvariant())
        {
            case "show":
                return BucketActionKind.ServerShow;
            case "set":
                return BucketActionKind.ServerSet;
            case "clear":
                return BucketActionKind.ServerClear;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return BucketActionKind.ClientSet;
        }

        throw new CommandException(
            "bucket",
            MessageService.GetArgsString("command-bucket-error-invalid_subcommand", "subcommand", raw));
    }

    private bool IsServerAction() =>
        this.Classify() is BucketActionKind.ServerShow or BucketActionKind.ServerSet or BucketActionKind.ServerClear;

    private CommandState RunClientCommand(CosmosClient client)
    {
        if (this.Id.HasValue || this.Percent.HasValue)
        {
            throw new CommandException("bucket", MessageService.GetString("command-bucket-error-unexpected_args"));
        }

        if (this.Classify() == BucketActionKind.ClientSet)
        {
            int bucket = int.Parse(this.Action!.Trim(), CultureInfo.InvariantCulture);
            if (!CheckBucket(bucket))
            {
                return new CommandState();
            }

            if (bucket == 0)
            {
                client.ClientOptions.ThroughputBucket = null;
                AnsiConsole.MarkupLine(MessageService.GetString("command-bucket-reset_bucket"));
            }
            else
            {
                client.ClientOptions.ThroughputBucket = bucket;
                AnsiConsole.MarkupLine(MessageService.GetString("command-bucket-switched_bucket", new Dictionary<string, object> { { "bucket", Theme.FormatTableValue(bucket.ToString(CultureInfo.InvariantCulture)) } }));
            }
        }
        else
        {
            WriteClientSelection(client.ClientOptions.ThroughputBucket);
        }

        return new CommandState();
    }

    private async Task<CommandState> ExecuteServerAsync(ConnectedState state, ShellInterpreter shell, string databaseName, string? containerName, CancellationToken token)
    {
        if (string.IsNullOrEmpty(containerName))
        {
            throw new CommandException("bucket", MessageService.GetString("command-bucket-error-container_required"));
        }

        await ValidateContainerExistsAsync(state, databaseName, containerName, "bucket", token);

        try
        {
            switch (this.Classify())
            {
                case BucketActionKind.ServerSet:
                    return await this.ExecuteSetAsync(state, shell, databaseName, containerName, token);
                case BucketActionKind.ServerClear:
                    return await this.ExecuteClearAsync(state, shell, databaseName, containerName, token);
                case BucketActionKind.ServerShow:
                default:
                    var view = await CosmosResourceFacade.GetThroughputBucketsAsync(state, databaseName, containerName, token);
                    return BuildShowResult(shell, state, view);
            }
        }
        catch (ThroughputBucketsNotSupportedException ex)
        {
            throw new CommandException("bucket", MessageService.GetString("command-bucket-error-arm_required"), ex);
        }
        catch (ThroughputNotConfiguredException ex)
        {
            throw new CommandException(
                "bucket",
                MessageService.GetArgsString("command-bucket-error-not_configured", "resource", ex.ResourceName),
                ex);
        }
    }

    private async Task<CommandState> ExecuteSetAsync(ConnectedState state, ShellInterpreter shell, string databaseName, string containerName, CancellationToken token)
    {
        int bucketId = this.RequireBucketId();
        int percent = this.RequirePercent();

        if (!ConfirmWrite(shell, this.Yes == true, "command-bucket-confirm_set_summary", "container", Markup.Escape(containerName), "id", bucketId, "percent", percent))
        {
            ShellInterpreter.WriteLine(MessageService.GetString("command-bucket-cancelled"));
            return new CommandState { IsPrinted = true };
        }

        var view = await CosmosResourceFacade.SetThroughputBucketAsync(state, databaseName, containerName, bucketId, percent, token);
        ShellInterpreter.WriteLine(MessageService.GetString("command-bucket-set_done"));
        return BuildShowResult(shell, state, view);
    }

    private async Task<CommandState> ExecuteClearAsync(ConnectedState state, ShellInterpreter shell, string databaseName, string containerName, CancellationToken token)
    {
        int bucketId = this.RequireBucketId();

        if (!ConfirmWrite(shell, this.Yes == true, "command-bucket-confirm_clear_summary", "container", Markup.Escape(containerName), "id", bucketId))
        {
            ShellInterpreter.WriteLine(MessageService.GetString("command-bucket-cancelled"));
            return new CommandState { IsPrinted = true };
        }

        var view = await CosmosResourceFacade.ClearThroughputBucketAsync(state, databaseName, containerName, bucketId, token);
        ShellInterpreter.WriteLine(MessageService.GetString("command-bucket-clear_done"));
        return BuildShowResult(shell, state, view);
    }

    private int RequireBucketId()
    {
        if (!this.Id.HasValue)
        {
            throw new CommandException("bucket", MessageService.GetString("command-bucket-error-missing_id"));
        }

        int id = this.Id.Value;
        if (id < 1 || id > 5)
        {
            throw new CommandException("bucket", MessageService.GetArgsString("command-bucket-error-invalid_id", "id", id));
        }

        return id;
    }

    private int RequirePercent()
    {
        if (!this.Percent.HasValue)
        {
            throw new CommandException("bucket", MessageService.GetString("command-bucket-error-missing_percent"));
        }

        int percent = this.Percent.Value;
        if (percent < 1 || percent > 100)
        {
            throw new CommandException("bucket", MessageService.GetArgsString("command-bucket-error-invalid_percent", "percent", percent));
        }

        return percent;
    }
}
