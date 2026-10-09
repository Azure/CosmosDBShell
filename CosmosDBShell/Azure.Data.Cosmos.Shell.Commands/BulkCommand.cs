// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

[CosmosCommand("bulk")]
[CosmosExample("bulk run '[{\"op\":\"upsert\",\"item\":{\"id\":\"1\",\"pk\":\"a\"}},{\"op\":\"delete\",\"id\":\"2\",\"partitionKey\":\"b\"}]' --concurrency 32 --yes", DescriptionKey = "command-bulk-example-1")]
[CosmosExample("bulk run operations.jsonl --journal operations.journal --yes", DescriptionKey = "command-bulk-example-2")]
[CosmosExample("bulk patch --where 'c.schemaVersion = 1' --operations '[{\"op\":\"set\",\"path\":\"/schemaVersion\",\"value\":2}]' --dry-run --save migration.jsonl", DescriptionKey = "command-bulk-example-3")]
[CosmosExample("bulk delete --where 'c.expired = true' --etag --yes", DescriptionKey = "command-bulk-example-4")]
[CosmosExample("bulk begin --partition-key a", DescriptionKey = "command-bulk-example-5")]
[CosmosExample("bulk add '{\"op\":\"patch\",\"id\":\"3\",\"operations\":[{\"op\":\"set\",\"path\":\"/status\",\"value\":\"done\"}]}'", DescriptionKey = "command-bulk-example-6")]
[CosmosExample("bulk execute --yes", DescriptionKey = "command-bulk-example-7")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Bulk",
    Description = @"
Executes many independent write operations with bounded concurrency. Unlike 'batch', operations may target different partition keys, there is no 100-operation limit, and nothing is rolled back: each operation succeeds or fails on its own.

Subcommands available through MCP:
- 'run <json-or-file>' executes operations. Same operation schema as batch, plus an optional per-operation 'partitionKey' (scalar, or array for hierarchical keys) and 'ifMatch' (ETag). The 'partition-key' argument sets a default for delete and patch operations without one.
- 'patch --where <predicate> --operations <json-array>' patches every item matching a Cosmos SQL predicate over alias c.
- 'delete --where <predicate>' deletes every matching item.

Use 'dry-run' to validate or select without writing; dry runs do not require confirmation. Writes always require user confirmation. Stateful subcommands (begin, add, execute, cancel, status, show) are interactive-shell only.
",
    Restricted = true,
    Destructive = true,
    Confirmable = true)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal sealed class BulkCommand : CosmosCommand
{
    internal const int DefaultConcurrency = 16;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static readonly IReadOnlyList<string> McpSubcommands = ["run", "patch", "delete"];

    private const int SelectionPageSize = 1000;

    private const int StatusPreviewCount = 100;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);

    private static readonly Dictionary<string, string[]> AllowedOptions = new()
    {
        ["run"] = ["partition-key", "database", "container", "max-ru", "dry-run", "yes", "continue-on-error", "journal", "retry-uncertain"],
        ["patch"] = ["where", "operations", "database", "container", "max-items", "max-ru", "dry-run", "yes", "continue-on-error", "etag", "save", "journal"],
        ["delete"] = ["where", "database", "container", "max-items", "max-ru", "dry-run", "yes", "continue-on-error", "etag", "save", "journal"],
        ["begin"] = ["partition-key", "database", "container"],
        ["add"] = [],
        ["execute"] = ["max-ru", "yes", "continue-on-error", "journal", "retry-uncertain"],
        ["cancel"] = [],
        ["status"] = [],
        ["show"] = [],
    };

    private BulkSummary? summary;

    [CosmosParameter("subcommand", RequiredErrorKey = "command-bulk-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("data", IsRequired = false)]
    public string? Data { get; init; }

    [CosmosOption("where")]
    public string? Where { get; init; }

    [CosmosOption("operations")]
    public string? Operations { get; init; }

    [CosmosOption("partition-key", "pk")]
    public string? PartitionKeyArgument { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("concurrency", "max-parallelism", DefaultValue = DefaultConcurrency)]
    public int? Concurrency { get; init; }

    [CosmosOption("max-items")]
    public long? MaxItems { get; init; }

    [CosmosOption("max-ru")]
    public double? MaxRu { get; init; }

    [CosmosOption("dry-run")]
    public bool? DryRun { get; init; }

    [CosmosOption("yes", "y")]
    public bool? Yes { get; init; }

    [CosmosOption("continue-on-error")]
    public bool? ContinueOnError { get; init; }

    [CosmosOption("etag")]
    public bool? ETag { get; init; }

    [CosmosOption("save")]
    public string? Save { get; init; }

    [CosmosOption("journal")]
    public string? Journal { get; init; }

    [CosmosOption("retry-uncertain")]
    public bool? RetryUncertain { get; init; }

    internal bool ConfirmationApproved { get; set; }

    internal static string NormalizeSubcommand(string? subcommand)
    {
        return subcommand?.Trim().ToLowerInvariant() switch
        {
            "exec" or "commit" => "execute",
            "abort" => "cancel",
            var value => value ?? string.Empty,
        };
    }

    public override async Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var subcommand = NormalizeSubcommand(this.Subcommand);
        this.Validate(subcommand);
        try
        {
            return subcommand switch
            {
                "run" => await this.RunAsync(shell, commandState, token),
                "patch" or "delete" => await this.RunSelectionAsync(shell, subcommand, token),
                "begin" => await this.BeginAsync(shell, token),
                "add" => await this.AddAsync(shell, commandState, token),
                "execute" => await this.ExecutePendingAsync(shell, token),
                "cancel" => Cancel(shell),
                "status" => Status(shell),
                _ => Show(shell),
            };
        }
        catch (Exception) when (this.summary is not null)
        {
            RequestChargeContext.Record(this.summary.RequestCharge);
            throw;
        }
    }

    internal static async Task<BulkOutcome> WriteAsync(Container container, BulkOperation operation, CancellationToken token)
    {
        try
        {
            using var document = JsonDocument.Parse(operation.Json);
            var root = document.RootElement;
            var key = BulkOperationNormalizer.ParseKey(operation.PartitionKey);
            var ifMatch = root.TryGetProperty("ifMatch", out var tag) ? tag.GetString() : null;
            var options = new ItemRequestOptions { IfMatchEtag = ifMatch, EnableContentResponseOnWrite = false };
            var item = root.TryGetProperty("item", out var value) ? value.Clone() : default;
            ItemResponse<JsonElement> response = operation.Op switch
            {
                "create" => await container.CreateItemAsync(item, key, options, token),
                "upsert" => await container.UpsertItemAsync(item, key, options, token),
                "replace" => await container.ReplaceItemAsync(item, operation.Id, key, options, token),
                "delete" => await container.DeleteItemAsync<JsonElement>(operation.Id, key, options, token),
                "patch" => await container.PatchItemAsync<JsonElement>(
                    operation.Id,
                    key,
                    BatchOperationParser.ParsePatchOperations("bulk", root),
                    new PatchItemRequestOptions { IfMatchEtag = ifMatch, EnableContentResponseOnWrite = false },
                    token),
                _ => throw new InvalidOperationException($"Unsupported bulk operation '{operation.Op}'."),
            };

            var status = (int)response.StatusCode;
            return BulkOutcome.Create(
                operation,
                status is >= 200 and < 300 ? BulkOutcome.Succeeded : BulkOutcome.Failed,
                response.RequestCharge,
                status);
        }
        catch (CosmosException ex)
        {
            // Timeouts and server errors can occur after the service applied the write.
            var uncertain = ex.StatusCode == HttpStatusCode.RequestTimeout || (int)ex.StatusCode >= 500;
            return BulkOutcome.Create(
                operation,
                uncertain ? BulkOutcome.Uncertain : BulkOutcome.Failed,
                ex.RequestCharge,
                (int)ex.StatusCode,
                CommandException.GetDisplayMessage(ex));
        }
    }

    internal static string BuildSelectionQuery(string where, IReadOnlyList<string> partitionKeyPaths)
    {
        var query = new StringBuilder("SELECT c.id, c._etag");
        for (var i = 0; i < partitionKeyPaths.Count; i++)
        {
            query.Append(", c");
            foreach (var segment in partitionKeyPaths[i].Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                query.Append('[').Append(JsonSerializer.Serialize(segment, JsonOptions)).Append(']');
            }

            query.Append(" AS __pk").Append(i.ToString(CultureInfo.InvariantCulture));
        }

        return query.Append(" FROM c WHERE (").Append(where).Append(')').ToString();
    }

    internal void Validate(string subcommand)
    {
        if (subcommand.Length == 0)
        {
            throw Error("command-bulk-error-missing_subcommand");
        }

        if (!AllowedOptions.TryGetValue(subcommand, out var allowed))
        {
            throw new CommandException("bulk", MessageService.GetArgsString("command-bulk-error-invalid_subcommand", "subcommand", subcommand));
        }

        foreach (var (name, supplied) in this.GetSuppliedOptions())
        {
            if (supplied && !allowed.Contains(name))
            {
                throw new CommandException("bulk", MessageService.GetArgsString("command-bulk-error-option_not_supported", "option", name, "subcommand", subcommand));
            }
        }

        if (this.Data != null && subcommand is not ("run" or "add"))
        {
            throw new CommandException("bulk", MessageService.GetArgsString("command-bulk-error-data_not_supported", "subcommand", subcommand));
        }

        if (this.Concurrency is < 1 || this.MaxItems is < 1 || (this.MaxRu is { } ru && (!double.IsFinite(ru) || ru <= 0)))
        {
            throw Error("command-bulk-error-invalid_number");
        }

        if (subcommand is "patch" or "delete" && string.IsNullOrWhiteSpace(this.Where))
        {
            throw new CommandException("bulk", MessageService.GetArgsString("command-bulk-error-missing_where", "subcommand", subcommand));
        }

        if (subcommand == "patch")
        {
            if (string.IsNullOrWhiteSpace(this.Operations))
            {
                throw Error("command-bulk-error-missing_operations");
            }

            try
            {
                BulkOperationNormalizer.ValidatePatchOperations(JsonSerializer.Deserialize<JsonElement>(this.Operations));
            }
            catch (JsonException ex)
            {
                throw new CommandException("bulk", MessageService.GetArgsString("command-batch-error-invalid_json", "message", ex.Message), ex);
            }
        }

        if (this.DryRun == true && (this.Journal != null || this.RetryUncertain == true))
        {
            throw new CommandException("bulk", MessageService.GetArgsString("command-bulk-error-dry_run_option", "option", this.Journal != null ? "journal" : "retry-uncertain"));
        }

        if (this.Journal != null && subcommand is "patch" or "delete" && this.Save == null)
        {
            throw Error("command-bulk-error-journal_requires_save");
        }

        if (this.RetryUncertain == true && subcommand == "run" && this.Journal == null)
        {
            throw Error("command-bulk-error-retry_requires_journal");
        }
    }

    private static CommandException Error(string key) => new("bulk", MessageService.GetString(key));

    private static ConnectedState RequireConnected(ShellInterpreter shell)
    {
        return shell.State as ConnectedState ?? throw new NotConnectedException("bulk");
    }

    private static bool CanPrompt(ShellInterpreter shell)
    {
        return !shell.IsMachineMode
            && shell.IsInteractiveSession()
            && string.IsNullOrEmpty(shell.CurrentScriptFileName)
            && shell.Options?.ExecuteAndQuit == null
            && shell.Options?.ExecuteAndContinue == null;
    }

    private static async IAsyncEnumerable<JsonElement> ReadOperationsAsync(string data, [EnumeratorCancellation] CancellationToken token)
    {
        var trimmed = data.TrimStart();
        if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
        {
            using var document = ParseInline(data);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    yield return element.Clone();
                }
            }
            else
            {
                yield return document.RootElement.Clone();
            }

            yield break;
        }

        if (!File.Exists(data))
        {
            throw new CommandException("bulk", MessageService.GetArgsString("command-bulk-error-file_not_found", "file", data));
        }

        await using var stream = new FileStream(data, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var isArray = await StartsWithArrayAsync(stream, token);
        stream.Position = 0;
        if (isArray)
        {
            await foreach (var (_, element) in ImportCommand.EnumerateArrayAsync(stream, token))
            {
                yield return element;
            }
        }
        else
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            await foreach (var (_, element) in ImportCommand.EnumerateJsonLinesAsync(reader, token))
            {
                yield return element;
            }
        }
    }

    private static JsonDocument ParseInline(string data)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException ex)
        {
            throw new CommandException("bulk", MessageService.GetArgsString("command-batch-error-invalid_json", "message", ex.Message), ex);
        }
    }

    private static async Task<bool> StartsWithArrayAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var value = buffer[i];
                if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0xEF or 0xBB or 0xBF)
                {
                    continue;
                }

                return value == (byte)'[';
            }
        }

        return false;
    }

    private static async IAsyncEnumerable<BulkOperation> EnumerateAsync(IReadOnlyList<BulkOperation> operations)
    {
        foreach (var operation in operations)
        {
            yield return operation;
        }

        await Task.CompletedTask;
    }

    private static StreamWriter OpenNewFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            return new StreamWriter(new FileStream(path, options), new UTF8Encoding(false)) { NewLine = "\n" };
        }
        catch (IOException ex) when (File.Exists(path))
        {
            throw new CommandException("bulk", MessageService.GetArgsString("command-bulk-error-save_exists", "file", path), ex);
        }
    }

    private static JsonElement CreateSelectedOperation(string subcommand, JsonElement row, int componentCount, JsonNode? patchOperations, bool useETag)
    {
        if (!row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
        {
            throw Error("command-bulk-error-selected_id");
        }

        var key = new JsonArray();
        for (var i = 0; i < componentCount; i++)
        {
            key.Add(row.TryGetProperty($"__pk{i}", out var component) ? JsonNode.Parse(component.GetRawText()) : new JsonObject());
        }

        var operation = new JsonObject
        {
            ["op"] = subcommand,
            ["id"] = id.GetString(),
            ["partitionKey"] = key,
        };

        if (useETag)
        {
            if (!row.TryGetProperty("_etag", out var etag) || etag.ValueKind != JsonValueKind.String)
            {
                throw Error("command-bulk-error-selected_etag");
            }

            operation["ifMatch"] = etag.GetString();
        }

        if (patchOperations != null)
        {
            operation["operations"] = patchOperations.DeepClone();
        }

        return JsonSerializer.SerializeToElement(operation);
    }

    private static CommandState CreateResult(BulkSummary summary)
    {
        var result = new ShellJson(JsonSerializer.SerializeToElement(summary, JsonOptions));
        var charge = summary.RequestCharge.ToString("F2", CultureInfo.InvariantCulture);
        if (summary.Success)
        {
            var message = summary.DryRun
                ? MessageService.GetArgsString("command-bulk-dry-run", "count", summary.OperationCount, "charge", charge)
                : MessageService.GetArgsString("command-bulk-success", "count", summary.OperationCount, "succeeded", summary.Succeeded, "skipped", summary.Skipped, "charge", charge);
            return new CommandState
            {
                Result = result,
                RequestCharge = summary.RequestCharge,
                RenderUser = () => ShellInterpreter.WriteLine(message),
            };
        }

        var error = summary.BudgetExceeded
            ? MessageService.GetArgsString("command-bulk-error-budget", "succeeded", summary.Succeeded, "count", summary.OperationCount, "charge", charge)
            : MessageService.GetArgsString("command-bulk-error-failed", "succeeded", summary.Succeeded, "failed", summary.Failed, "uncertain", summary.Uncertain, "charge", charge);
        var state = new StructuredErrorCommandState(
            new CommandException("bulk", error, new RequestFailedException(summary.BudgetExceeded ? 429 : 400, error)),
            result)
        {
            RequestCharge = summary.RequestCharge,
        };
        state.RenderUser = () =>
        {
            ShellInterpreter.WriteLine(error);
            foreach (var failure in summary.Errors.Take(5))
            {
                ShellInterpreter.WriteLine(MessageService.GetArgsString(
                    "command-bulk-error-item",
                    "index",
                    failure.Index + 1,
                    "op",
                    failure.Op,
                    "id",
                    failure.Id,
                    "status",
                    failure.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? failure.Status,
                    "message",
                    failure.Error ?? failure.Status));
            }
        };
        return state;
    }

    private static CommandState Cancel(ShellInterpreter shell)
    {
        var bulk = shell.CurrentBulk ?? throw Error("command-bulk-error-not_active");
        shell.CurrentBulk = null;
        var message = MessageService.GetArgsString("command-bulk-cancelled", "count", bulk.Operations.Count);
        return new CommandState { RenderUser = () => ShellInterpreter.WriteLine(message) };
    }

    private static CommandState Status(ShellInterpreter shell)
    {
        var bulk = shell.CurrentBulk;
        JsonObject root;
        Action renderUser;
        if (bulk is null)
        {
            root = new JsonObject { ["active"] = false };
            renderUser = () => ShellInterpreter.WriteLine(MessageService.GetString("command-bulk-status-inactive"));
        }
        else
        {
            var operations = new JsonArray();
            foreach (var operation in bulk.Operations.Take(StatusPreviewCount))
            {
                operations.Add(new JsonObject { ["op"] = operation.Op, ["id"] = operation.Id });
            }

            root = new JsonObject
            {
                ["active"] = true,
                ["database"] = bulk.DatabaseName,
                ["container"] = bulk.ContainerName,
                ["partitionKey"] = bulk.PartitionKeyArgument,
                ["operationCount"] = bulk.Operations.Count,
                ["succeeded"] = bulk.Outcomes.Values.Count(outcome => outcome.Status == BulkOutcome.Succeeded),
                ["failed"] = bulk.Outcomes.Values.Count(outcome => outcome.Status == BulkOutcome.Failed),
                ["uncertain"] = bulk.Outcomes.Values.Count(outcome => outcome.Status is BulkOutcome.Started or BulkOutcome.Uncertain),
                ["operations"] = operations,
            };
            renderUser = () => RenderStatus(bulk);
        }

        using var document = JsonDocument.Parse(root.ToJsonString());
        return new CommandState { Result = new ShellJson(document.RootElement.Clone()), RenderUser = renderUser };
    }

    private static void RenderStatus(PendingBulkState bulk)
    {
        var details = new Table().HideHeaders();
        details.AddColumn(string.Empty);
        details.AddColumn(string.Empty);
        void AddDetail(string labelKey, string value) =>
            details.AddRow(
                Theme.FormatHelpName(Markup.Escape(MessageService.GetString(labelKey))),
                Theme.FormatTableValue(Markup.Escape(value)));

        AddDetail("command-batch-status-target", $"{bulk.DatabaseName}/{bulk.ContainerName}");
        if (bulk.PartitionKeyArgument != null)
        {
            AddDetail("command-batch-status-partition-key", bulk.PartitionKeyArgument);
        }

        AddDetail("command-batch-status-operation-count", bulk.Operations.Count.ToString(CultureInfo.InvariantCulture));
        AnsiConsole.Write(details);
        if (bulk.Operations.Count == 0)
        {
            return;
        }

        var operations = new Table();
        operations.AddColumn(Theme.FormatSectionHeader(MessageService.GetString("command-batch-status-column-index")));
        operations.AddColumn(Theme.FormatSectionHeader(MessageService.GetString("command-batch-status-column-operation")));
        operations.AddColumn(Theme.FormatSectionHeader(MessageService.GetString("command-batch-status-column-id")));
        foreach (var operation in bulk.Operations.Take(StatusPreviewCount))
        {
            operations.AddRow(
                Theme.FormatTableValue((operation.Index + 1).ToString(CultureInfo.InvariantCulture)),
                Theme.FormatTableValue(Markup.Escape(operation.Op)),
                Theme.FormatTableValue(Markup.Escape(operation.Id)));
        }

        AnsiConsole.Write(operations);
        if (bulk.Operations.Count > StatusPreviewCount)
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-bulk-status-more", "count", bulk.Operations.Count - StatusPreviewCount));
        }
    }

    private static CommandState Show(ShellInterpreter shell)
    {
        var operations = new JsonArray();
        if (shell.CurrentBulk is { } bulk)
        {
            foreach (var operation in bulk.Operations)
            {
                operations.Add(JsonNode.Parse(operation.Json));
            }
        }

        using var document = JsonDocument.Parse(operations.ToJsonString(JsonOptions));
        return new CommandState { Result = new ShellJson(document.RootElement.Clone()) };
    }

    private IEnumerable<(string Name, bool Supplied)> GetSuppliedOptions()
    {
        return
        [
            ("where", this.Where != null),
            ("operations", this.Operations != null),
            ("partition-key", this.PartitionKeyArgument != null),
            ("database", this.Database != null),
            ("container", this.Container != null),
            ("max-items", this.MaxItems != null),
            ("max-ru", this.MaxRu != null),
            ("dry-run", this.DryRun == true),
            ("yes", this.Yes == true),
            ("continue-on-error", this.ContinueOnError == true),
            ("etag", this.ETag == true),
            ("save", this.Save != null),
            ("journal", this.Journal != null),
            ("retry-uncertain", this.RetryUncertain == true),
        ];
    }

    private string ResolveData(CommandState commandState)
    {
        var data = this.Data ?? commandState.Result?.ConvertShellObject(DataType.Text) as string;
        return string.IsNullOrWhiteSpace(data) ? throw Error("command-bulk-error-missing_data") : data;
    }

    private void RequireApprovalAvailable(ShellInterpreter shell)
    {
        if (this.DryRun != true && !this.ConfirmationApproved && this.Yes != true && !CanPrompt(shell))
        {
            throw Error("command-bulk-error-confirm_required");
        }
    }

    private void Confirm(ShellInterpreter shell, long count, Target target)
    {
        if (this.ConfirmationApproved || this.Yes == true)
        {
            return;
        }

        if (!CanPrompt(shell))
        {
            throw Error("command-bulk-error-confirm_required");
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-bulk-confirm-summary",
            "count",
            count,
            "target",
            $"{target.DatabaseName}/{target.ContainerName}"));
        if (!ShellInterpreter.Confirm("command-bulk-confirm"))
        {
            throw Error("command-bulk-error-declined");
        }
    }

    private async Task<Target> ResolveTargetAsync(ConnectedState connected, State state, string? database, string? container, CancellationToken token)
    {
        var (databaseName, containerName, reference) = ResolveContainerReference(connected.Client, state, database, container, "bulk");
        using var response = await reference.ReadContainerStreamAsync(cancellationToken: token);
        this.summary = new BulkSummary { DryRun = this.DryRun == true, RequestCharge = response.Headers.RequestCharge };
        if (!response.IsSuccessStatusCode)
        {
            var message = MessageService.GetArgsString(
                "command-bulk-error-container",
                "target",
                $"{databaseName}/{containerName}",
                "status",
                (int)response.StatusCode);
            throw new CommandException("bulk", message, new RequestFailedException((int)response.StatusCode, message));
        }

        using var metadata = await JsonDocument.ParseAsync(response.Content, cancellationToken: token);
        var root = metadata.RootElement;
        string[] paths = root.TryGetProperty("partitionKey", out var definition) && definition.TryGetProperty("paths", out var values)
            ? values.EnumerateArray().Select(path => path.GetString()!).ToArray()
            : [];
        return new Target(databaseName, containerName, reference, connected.Client.Endpoint.ToString(), root.GetProperty("_rid").GetString()!, paths);
    }

    private async Task<CommandState> RunAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var data = this.ResolveData(commandState);
        var connected = RequireConnected(shell);
        this.RequireApprovalAvailable(shell);
        var target = await this.ResolveTargetAsync(connected, shell.State, this.Database, this.Container, token);
        var defaultKey = this.PartitionKeyArgument is null
            ? null
            : BulkOperationNormalizer.CanonicalKeyFromArgument(this.PartitionKeyArgument, target.PartitionKeyPaths.Count);

        using var spool = BulkSpool.Create();
        await foreach (var element in ReadOperationsAsync(data, token))
        {
            var operation = BulkOperationNormalizer.Normalize(element, target.PartitionKeyPaths, defaultKey, spool.Count);
            await spool.AddAsync(operation.Json);
        }

        if (spool.Count == 0)
        {
            throw Error("command-bulk-error-empty");
        }

        return await this.ExecuteOperationsAsync(shell, target, spool.Count, spool.GetHash(), spool.ReadAsync(token), null, token);
    }

    private async Task<CommandState> RunSelectionAsync(ShellInterpreter shell, string subcommand, CancellationToken token)
    {
        var connected = RequireConnected(shell);
        this.RequireApprovalAvailable(shell);
        var target = await this.ResolveTargetAsync(connected, shell.State, this.Database, this.Container, token);
        var summary = this.summary!;
        var patchOperations = subcommand == "patch" ? JsonNode.Parse(this.Operations!) : null;

        using var spool = BulkSpool.Create();
        var savePath = this.Save is null ? null : Path.GetFullPath(this.Save);
        var save = savePath is null ? null : OpenNewFile(savePath);
        var complete = false;
        try
        {
            var query = BuildSelectionQuery(this.Where!, target.PartitionKeyPaths);
            using var iterator = target.Container.GetItemQueryIterator<JsonElement>(
                new QueryDefinition(query),
                continuationToken: null,
                new QueryRequestOptions { MaxItemCount = SelectionPageSize });
            while (iterator.HasMoreResults && !summary.SelectionLimited)
            {
                if (this.MaxRu is { } budget && summary.RequestCharge >= budget)
                {
                    summary.ResultIncomplete = true;
                    summary.BudgetExceeded = true;
                    break;
                }

                var page = await iterator.ReadNextAsync(token);
                summary.RequestCharge += page.RequestCharge;
                foreach (var row in page)
                {
                    if (this.MaxItems is { } max && spool.Count >= max)
                    {
                        summary.SelectionLimited = true;
                        break;
                    }

                    var element = CreateSelectedOperation(subcommand, row, target.PartitionKeyPaths.Count, patchOperations, this.ETag == true);
                    var operation = BulkOperationNormalizer.Normalize(element, target.PartitionKeyPaths, null, spool.Count);
                    await spool.AddAsync(operation.Json);
                    if (save != null)
                    {
                        await save.WriteLineAsync(operation.Json);
                    }
                }

                if (this.MaxItems is { } limit && spool.Count >= limit && iterator.HasMoreResults)
                {
                    summary.SelectionLimited = true;
                }
            }

            complete = !summary.ResultIncomplete;
        }
        finally
        {
            if (save != null)
            {
                await save.DisposeAsync();
                if (!complete)
                {
                    File.Delete(savePath!);
                }
            }
        }

        if (!complete)
        {
            summary.OperationCount = spool.Count;
            return CreateResult(summary);
        }

        return await this.ExecuteOperationsAsync(shell, target, spool.Count, spool.GetHash(), spool.ReadAsync(token), null, token);
    }

    private async Task<CommandState> BeginAsync(ShellInterpreter shell, CancellationToken token)
    {
        if (shell.CurrentBulk is not null)
        {
            throw Error("command-bulk-error-already_active");
        }

        var connected = RequireConnected(shell);
        var target = await this.ResolveTargetAsync(connected, shell.State, this.Database, this.Container, token);
        var defaultKey = this.PartitionKeyArgument is null
            ? null
            : BulkOperationNormalizer.CanonicalKeyFromArgument(this.PartitionKeyArgument, target.PartitionKeyPaths.Count);
        shell.CurrentBulk = new PendingBulkState(
            target.DatabaseName,
            target.ContainerName,
            target.ContainerRid,
            target.PartitionKeyPaths,
            this.PartitionKeyArgument,
            defaultKey);

        var message = MessageService.GetArgsString("command-bulk-begun", "database", target.DatabaseName, "container", target.ContainerName);
        return new CommandState
        {
            RequestCharge = this.summary!.RequestCharge,
            RenderUser = () => ShellInterpreter.WriteLine(message),
        };
    }

    private async Task<CommandState> AddAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var bulk = shell.CurrentBulk ?? throw Error("command-bulk-error-not_active");
        var data = this.ResolveData(commandState);
        var added = new List<BulkOperation>();
        await foreach (var element in ReadOperationsAsync(data, token))
        {
            added.Add(BulkOperationNormalizer.Normalize(element, bulk.PartitionKeyPaths, bulk.DefaultPartitionKey, bulk.Operations.Count + added.Count));
        }

        if (added.Count == 0)
        {
            throw Error("command-bulk-error-empty");
        }

        bulk.Operations.AddRange(added);
        var message = MessageService.GetArgsString("command-bulk-added", "count", added.Count, "total", bulk.Operations.Count);
        return new CommandState { RenderUser = () => ShellInterpreter.WriteLine(message) };
    }

    private async Task<CommandState> ExecutePendingAsync(ShellInterpreter shell, CancellationToken token)
    {
        var bulk = shell.CurrentBulk ?? throw Error("command-bulk-error-not_active");
        if (bulk.Operations.Count == 0)
        {
            throw Error("command-bulk-error-empty");
        }

        var connected = RequireConnected(shell);
        this.RequireApprovalAvailable(shell);
        var target = await this.ResolveTargetAsync(connected, shell.State, bulk.DatabaseName, bulk.ContainerName, token);
        if (target.ContainerRid != bulk.ContainerRid)
        {
            throw Error("command-bulk-error-container_changed");
        }

        var hash = BulkSpool.ComputeHash(bulk.Operations.Select(operation => operation.Json));
        var result = await this.ExecuteOperationsAsync(shell, target, bulk.Operations.Count, hash, EnumerateAsync(bulk.Operations), bulk.Outcomes, token);
        if (!result.IsError)
        {
            shell.CurrentBulk = null;
        }

        return result;
    }

    private async Task<CommandState> ExecuteOperationsAsync(
        ShellInterpreter shell,
        Target target,
        long count,
        string hash,
        IAsyncEnumerable<BulkOperation> operations,
        Dictionary<long, BulkOutcome>? memory,
        CancellationToken token)
    {
        var summary = this.summary!;
        summary.OperationCount = count;
        if (summary.DryRun || count == 0)
        {
            return CreateResult(summary);
        }

        this.Confirm(shell, count, target);
        using var journal = this.Journal is null
            ? null
            : BulkJournal.Open(this.Journal, target.Endpoint, target.ContainerRid, hash, count);
        var previous = new Dictionary<long, BulkOutcome>();
        foreach (var source in new[] { journal?.Previous, memory })
        {
            foreach (var (index, outcome) in source ?? Enumerable.Empty<KeyValuePair<long, BulkOutcome>>())
            {
                previous[index] = outcome;
            }
        }

        var progress = Stopwatch.StartNew();
        async Task RecordAsync(BulkOutcome outcome)
        {
            if (memory != null)
            {
                memory[outcome.Index] = outcome;
            }

            if (journal != null)
            {
                await journal.RecordAsync(outcome);
            }

            if (outcome.Status != BulkOutcome.Started && progress.Elapsed >= ProgressInterval && !shell.IsMachineMode)
            {
                progress.Restart();
                ShellInterpreter.WriteLine(MessageService.GetArgsString(
                    "command-bulk-progress",
                    "processed",
                    summary.Processed,
                    "total",
                    count,
                    "failed",
                    summary.Failed,
                    "charge",
                    summary.RequestCharge.ToString("F2", CultureInfo.InvariantCulture)));
            }
        }

        await BulkExecutor.ExecuteAsync(
            operations,
            (operation, cancellation) => WriteAsync(target.Container, operation, cancellation),
            RecordAsync,
            previous,
            summary,
            this.Concurrency ?? DefaultConcurrency,
            this.MaxRu,
            this.ContinueOnError == true,
            this.RetryUncertain == true,
            token);
        return CreateResult(summary);
    }

    private sealed record Target(
        string DatabaseName,
        string ContainerName,
        Container Container,
        string Endpoint,
        string ContainerRid,
        IReadOnlyList<string> PartitionKeyPaths);
}
