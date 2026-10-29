//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Spectre.Console;

[CosmosCommand("rm")]
[CosmosExample("rm test-*", DescriptionKey = "command-rm-example-1")]
[CosmosExample("rm *-temp", DescriptionKey = "command-rm-example-2")]
[CosmosExample("rm old-item-* --key=id", DescriptionKey = "command-rm-example-3")]
[CosmosExample("rm test-* --database=MyDB --container=Items", DescriptionKey = "command-rm-example-4")]
[CosmosExample("rm test-* --dry-run", DescriptionKey = "command-rm-example-5")]
[CosmosExample("rm order-123 --key=id --partition-key=customer-42 --dry-run", DescriptionKey = "command-rm-example-6")]
[CosmosExample("rm order-123 --key=id --partition-key=customer-42 --etag '\"<etag-from-dry-run>\"'", DescriptionKey = "command-rm-example-7")]
[McpAnnotation(Title = "Remove Items", Restricted = true, Destructive = true, Confirmable = true)]
internal class RmCommand : CosmosCommand, IStateVisitor<ExitCode, CommandState>
{
    private const string IdPropertyName = "id";

    private PatternMatcher? matcher;
    private ShellInterpreter? shell;
    private PartitionKey? partitionKey;
    private string? partitionKeyArgument;
    private string? etag;

    [CosmosParameter("pattern")]
    public string? Pattern { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("key", "k")]
    public string? Key { get; init; }

    [CosmosOption("dry-run")]
    public bool? DryRun { get; init; }

    // Track whether these safety options were supplied at all: MCP binds an explicit
    // JSON null as a null value, which must not be treated as an omitted option.
    [CosmosOption("partition-key", "pk")]
    public string? PartitionKeyArgument
    {
        get
        {
            return this.partitionKeyArgument;
        }

        init
        {
            this.partitionKeyArgument = value;
            this.PartitionKeySpecified = true;
        }
    }

    [CosmosOption("etag")]
    public string? ETag
    {
        get
        {
            return this.etag;
        }

        init
        {
            this.etag = value;
            this.ETagSpecified = true;
        }
    }

    internal bool PartitionKeySpecified { get; private set; }

    internal bool ETagSpecified { get; private set; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        bool hasPipeInput = commandState.Result != null;
        if (!hasPipeInput)
        {
            if (string.IsNullOrEmpty(this.Pattern))
            {
                throw new CommandException("rm", MessageService.GetString("command-rm-error-no_filter"));
            }
        }

        this.ValidateETagOptions(hasPipeInput);
        this.partitionKey = this.ParsePartitionKeyArgument();

        this.shell = shell;
        this.matcher = string.IsNullOrEmpty(this.Pattern) ? null : new PatternMatcher(this.Pattern);
        await shell.State.AcceptAsync(this, commandState, token);
        return commandState;
    }

    Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitDisconnectedStateAsync(DisconnectedState state, CommandState commandState, CancellationToken token)
    {
        throw new NotConnectedException("rm");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitConnectedStateAsync(ConnectedState state, CommandState commandState, CancellationToken token)
    {
        // If both database and container are specified, allow removing items
        if (!string.IsNullOrEmpty(this.Database) && !string.IsNullOrEmpty(this.Container))
        {
            return await this.RemoveItemsFromContainerAsync(state, this.Database, this.Container, commandState, token);
        }

        throw new NotInContainerException("rm");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitDatabaseStateAsync(DatabaseState state, CommandState commandState, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        if (!string.IsNullOrEmpty(this.Container))
        {
            return await this.RemoveItemsFromContainerAsync(state, databaseName, this.Container, commandState, token);
        }

        throw new NotInContainerException("rm");
    }

    async Task<ExitCode> IStateVisitor<ExitCode, CommandState>.VisitContainerStateAsync(ContainerState state, CommandState commandState, CancellationToken token)
    {
        string databaseName = this.Database ?? state.DatabaseName;
        string containerName = this.Container ?? state.ContainerName;

        return await this.RemoveItemsFromContainerAsync(state, databaseName, containerName, commandState, token);
    }

    private async Task<ExitCode> RemoveItemsFromContainerAsync(ConnectedState state, string databaseName, string containerName, CommandState commandState, CancellationToken token)
    {
        if (this.shell == null)
        {
            throw new CommandException("rm", MessageService.GetString("error-shell-not-initialized"));
        }

        // Handle both pattern matching and pipe input
        bool hasPipeInput = commandState.Result != null;
        if (this.matcher == null && !hasPipeInput)
        {
            throw new CommandException("rm", MessageService.GetString("command-rm-error-no_filter"));
        }

        // Validate database and container exist
        await ValidateContainerExistsAsync(state, databaseName, containerName, "rm", token);

        var client = state.Client;
        var container = client.GetDatabase(databaseName).GetContainer(containerName);

        // Get container properties to find the partition key paths
        var partitionKeyPaths = await CosmosResourceFacade.GetPartitionKeyPathsAsync(state, databaseName, containerName, token);
        var partitionKeyPropertyNames = GetPartitionKeyPropertyNames(partitionKeyPaths);

        if (this.partitionKey is PartitionKey scopedPartitionKey)
        {
            var componentCount = GetPartitionKeyComponentCount(scopedPartitionKey);
            if (componentCount != partitionKeyPropertyNames.Length)
            {
                throw new CommandException(
                    "rm",
                    MessageService.GetString(
                        "command-rm-error-partition_key_components",
                        new Dictionary<string, object>
                        {
                            { "expected", partitionKeyPropertyNames.Length },
                            { "actual", componentCount },
                        }));
            }

            if (!hasPipeInput && this.IsExactIdTarget())
            {
                return await this.RemoveExactItemAsync(container, scopedPartitionKey, commandState, token);
            }
        }

        // Determine which key to match against (partition key by default, or custom key if specified)
        var matchKeyPropertyNames = string.IsNullOrEmpty(this.Key) ? partitionKeyPropertyNames : [this.Key];

        var totalCount = 0;
        var scannedCount = 0;
        double totalCharge = 0;
        bool dryRun = this.DryRun == true;
        var progress = new IngestionProgress(
            this.shell,
            new CommandState(),
            dryRun ? "command-rm-dry-run-progress" : "command-rm-progress");

        void ReportProgress() => progress.Update(scannedCount, totalCount, 0, totalCharge);

        void RecordDelete((bool Counted, double RequestCharge) result)
        {
            totalCharge += result.RequestCharge;
            if (result.Counted)
            {
                totalCount++;
            }

            ReportProgress();
        }

        bool ShouldDelete(JsonElement element)
        {
            return this.matcher == null
                || MatchesAnyPath(element, matchKeyPropertyNames, this.matcher)
                || this.Pattern == "*";
        }

        // Piped records may be projections that omit key properties, so a missing key is not
        // inferred as undefined unless the caller explicitly scoped the delete with --partition-key.
        bool TryGetPipedItemPartitionKey(JsonElement item, out PartitionKey itemPartitionKey) =>
            TryCreatePartitionKeyFromItem(item, partitionKeyPropertyNames, out var hasUndefinedComponent, out itemPartitionKey)
                && (!hasUndefinedComponent || this.partitionKey is not null);

        // In dry-run mode, count what would be deleted without issuing any delete.
        async Task<(bool Counted, double RequestCharge)> TryDeleteAsync(string id, PartitionKey itemPartitionKey)
        {
            if (this.partitionKey is PartitionKey scope && !scope.Equals(itemPartitionKey))
            {
                return (false, 0);
            }

            if (dryRun)
            {
                return (true, 0);
            }

            try
            {
                var deleteResponse = await container.DeleteItemAsync<object>(id, itemPartitionKey, cancellationToken: token);
                return (true, deleteResponse.RequestCharge);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Item was already deleted, skip
                return (false, ex.RequestCharge);
            }
        }

        async Task RemoveMatchesAsync()
        {
            // Process pipe input if available
            if (hasPipeInput && commandState.Result is ShellJson jsonResult)
            {
                // Handle items from pipe - check if it's an array or object with items property
                JsonElement itemsArray;

                if (jsonResult.Value.ValueKind == JsonValueKind.Array)
                {
                    itemsArray = jsonResult.Value;
                }
                else if (jsonResult.Value.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                {
                    itemsArray = itemsProp;
                }
                else
                {
                    // Single item - treat as array of one
                    itemsArray = jsonResult.Value;
                }

                if (itemsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in itemsArray.EnumerateArray())
                    {
                        token.ThrowIfCancellationRequested();
                        scannedCount++;
                        this.shell.ReportOperationProgress();

                        // Filter before converting the key so an excluded item with an invalid
                        // partition-key value cannot block deletion of matching items.
                        if (item.TryGetProperty("id", out var idElement)
                            && idElement.GetString() is { } id
                            && ShouldDelete(item)
                            && TryGetPipedItemPartitionKey(item, out var itemPartitionKey))
                        {
                            RecordDelete(await TryDeleteAsync(id, itemPartitionKey));
                        }
                        else
                        {
                            ReportProgress();
                        }
                    }
                }
                else
                {
                    // Single item from pipe
                    token.ThrowIfCancellationRequested();
                    scannedCount++;
                    this.shell.ReportOperationProgress();
                    if (itemsArray.TryGetProperty("id", out var idElement)
                        && idElement.GetString() is { } id
                        && ShouldDelete(itemsArray)
                        && TryGetPipedItemPartitionKey(itemsArray, out var itemPartitionKey))
                    {
                        RecordDelete(await TryDeleteAsync(id, itemPartitionKey));
                    }
                }
            }
            else if (this.matcher != null)
            {
                var needsDeletionPlan = !dryRun
                    && this.partitionKey == null
                    && partitionKeyPropertyNames.Length > 0;
                var queryOptions = this.partitionKey is PartitionKey scope
                    ? new QueryRequestOptions { PartitionKey = scope }
                    : null;

                using var feedIterator = container.GetItemQueryStreamIterator("SELECT * FROM c", requestOptions: queryOptions);
                await using var planStream = needsDeletionPlan ? this.CreateDeletionPlanStream() : null;
                await using var planWriter = planStream is null
                    ? null
                    : new StreamWriter(planStream, new System.Text.UTF8Encoding(false), 4096, leaveOpen: true);
                var undefinedPartitionKeyCount = 0;

                while (feedIterator.HasMoreResults)
                {
                    token.ThrowIfCancellationRequested();
                    using var response = await feedIterator.ReadNextAsync(token);

                    // The scan pages that locate matching items consume RUs regardless of whether
                    // any item is ultimately deleted (including in --dry-run), so include each
                    // page's request charge from the response headers.
                    totalCharge += response.Headers.RequestCharge;

                    using var streamReader = new StreamReader(response.Content);
                    using var queryDocument = JsonDocument.Parse(await streamReader.ReadToEndAsync(token));

                    foreach (var element in queryDocument.RootElement.GetProperty("Documents").EnumerateArray())
                    {
                        scannedCount++;
                        if (!element.TryGetProperty(IdPropertyName, out var idElement)
                            || idElement.ValueKind != JsonValueKind.String
                            || idElement.GetString() is not string id
                            || !ShouldDelete(element))
                        {
                            continue;
                        }

                        TryCreatePartitionKeyFromItem(
                            element,
                            partitionKeyPropertyNames,
                            out var hasUndefinedComponent,
                            out var itemPartitionKey);

                        if (planWriter != null)
                        {
                            if (hasUndefinedComponent)
                            {
                                undefinedPartitionKeyCount++;
                            }

                            var partitionKeyJson = itemPartitionKey.Equals(PartitionKey.None)
                                ? "{}"
                                : itemPartitionKey.ToString();
                            var candidate = new DeletionCandidate(id, partitionKeyJson);
                            await planWriter.WriteLineAsync(JsonSerializer.Serialize(candidate).AsMemory(), token);
                        }
                        else
                        {
                            RecordDelete(await TryDeleteAsync(id, itemPartitionKey));
                        }
                    }

                    ReportProgress();
                }

                token.ThrowIfCancellationRequested();
                if (planWriter != null)
                {
                    await planWriter.FlushAsync(token);
                    planStream!.Position = 0;

                    AddUndefinedPartitionKeyWarning(commandState, undefinedPartitionKeyCount);
                    this.shell!.RenderMutationWarnings(commandState);

                    using var planReader = new StreamReader(planStream, System.Text.Encoding.UTF8, false, 4096, leaveOpen: true);
                    while (await planReader.ReadLineAsync(token) is { } line)
                    {
                        token.ThrowIfCancellationRequested();
                        var candidate = JsonSerializer.Deserialize<DeletionCandidate>(line)
                            ?? throw new JsonException("Unable to read a delete candidate from the temporary plan.");
                        var itemPartitionKey = CreatePartitionKeyFromArgument(candidate.PartitionKeyJson);
                        RecordDelete(await TryDeleteAsync(candidate.Id, itemPartitionKey));
                    }
                }
            }
        }

        await progress.RunAsync(RemoveMatchesAsync);

        string renderMessage = totalCount > 0
            ? MessageService.GetString(
                dryRun ? "command-rm-dry-run-plan" : "command-rm-deleted_items",
                new Dictionary<string, object> { { "count", totalCount } })
            : MessageService.GetString(
                "command-rm-no-matches",
                new Dictionary<string, object>
                {
                    { "pattern", this.Pattern ?? "pipe input" },
                    { "key", string.Join(',', matchKeyPropertyNames) },
                });

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(this.CreateResult(totalCount, dryRun)));
        commandState.RenderUser = () =>
        {
            AnsiConsole.MarkupLine(renderMessage);
        };

        commandState.RequestCharge = totalCharge > 0 ? totalCharge : null;
        return new ExitCode(0);
    }

    internal static PartitionKey ParsePartitionKey(string rawValue) =>
        CreatePartitionKeyFromArgument(rawValue, "rm", "command-rm-error-invalid_pk_json");

    private static bool HasWildcard(string pattern) => pattern.IndexOfAny(['*', '?']) >= 0;

    private bool IsExactIdTarget()
    {
        return string.Equals(this.Key, IdPropertyName, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(this.Pattern)
            && !HasWildcard(this.Pattern);
    }

    private void ValidateETagOptions(bool hasPipeInput)
    {
        if (this.PartitionKeySpecified && this.PartitionKeyArgument == null)
        {
            throw new CommandException("rm", MessageService.GetString("command-rm-error-partition_key_missing_value"));
        }

        if (!this.ETagSpecified)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(this.ETag))
        {
            throw new CommandException("rm", MessageService.GetString("command-rm-error-etag_empty"));
        }

        if (hasPipeInput || !this.IsExactIdTarget())
        {
            throw new CommandException("rm", MessageService.GetString("command-rm-error-etag_requires_exact_id"));
        }

        if (this.PartitionKeyArgument == null)
        {
            throw new CommandException("rm", MessageService.GetString("command-rm-error-etag_requires_partition_key"));
        }
    }

    private PartitionKey? ParsePartitionKeyArgument()
    {
        return this.PartitionKeyArgument == null ? null : ParsePartitionKey(this.PartitionKeyArgument);
    }

    private async Task<ExitCode> RemoveExactItemAsync(Container container, PartitionKey targetPartitionKey, CommandState commandState, CancellationToken token)
    {
        var id = this.Pattern!;
        var dryRun = this.DryRun == true;
        string? currentETag = null;
        double requestCharge;
        int count;

        if (dryRun)
        {
            using var response = await container.ReadItemStreamAsync(id, targetPartitionKey, cancellationToken: token);
            requestCharge = response.Headers.RequestCharge;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                count = 0;
            }
            else
            {
                response.EnsureSuccessStatusCode();
                currentETag = response.Headers.ETag;
                if (this.ETag != null && !string.Equals(this.ETag, currentETag, StringComparison.Ordinal))
                {
                    throw new CommandException("rm", GetETagMismatchMessage(id));
                }

                count = 1;
            }
        }
        else
        {
            var requestOptions = this.ETag == null ? null : new ItemRequestOptions { IfMatchEtag = this.ETag };
            try
            {
                var response = await container.DeleteItemAsync<object>(id, targetPartitionKey, requestOptions, token);
                requestCharge = response.RequestCharge;
                count = 1;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                requestCharge = ex.RequestCharge;
                count = 0;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
            {
                throw new CommandException("rm", GetETagMismatchMessage(id), ex);
            }
        }

        var partitionKeyJson = ToPartitionKeyJson(targetPartitionKey);
        var result = this.CreateResult(count, dryRun);
        result["id"] = id;
        if (currentETag != null)
        {
            result["etag"] = currentETag;
        }

        string renderMessage;
        if (count == 0)
        {
            renderMessage = MessageService.GetString(
                "command-rm-no-matches",
                new Dictionary<string, object>
                {
                    { "pattern", id },
                    { "key", IdPropertyName },
                });
        }
        else
        {
            renderMessage = MessageService.GetString(
                dryRun ? "command-rm-dry-run-plan" : "command-rm-deleted_items",
                new Dictionary<string, object> { { "count", count } });
            if (dryRun)
            {
                renderMessage += Environment.NewLine + MessageService.GetString(
                    "command-rm-dry-run-item",
                    new Dictionary<string, object>
                    {
                        { "id", id },
                        { "partitionKey", partitionKeyJson.GetRawText() },
                        { "etag", currentETag ?? string.Empty },
                    });
            }
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(result));
        commandState.RenderUser = () => AnsiConsole.WriteLine(renderMessage);
        commandState.RequestCharge = requestCharge > 0 ? requestCharge : null;
        return new ExitCode(0);

        static string GetETagMismatchMessage(string id) => MessageService.GetString(
            "command-rm-error-etag_mismatch",
            new Dictionary<string, object> { { "id", id } });
    }

    private Dictionary<string, object?> CreateResult(int count, bool dryRun)
    {
        var result = new Dictionary<string, object?>
        {
            ["type"] = "item",
            ["count"] = count,
            ["dryRun"] = dryRun,
        };

        if (this.partitionKey is PartitionKey scope)
        {
            result["partitionKey"] = ToPartitionKeyJson(scope);
        }

        if (this.ETag != null)
        {
            result["ifMatchEtag"] = this.ETag;
        }

        return result;
    }

    private FileStream CreateDeletionPlanStream()
    {
        var path = Path.Join(Path.GetTempPath(), $"cosmosdbshell-rm-{Guid.NewGuid():N}.jsonl");
        const FileOptions options = FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan;

        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ?? throw new UnauthorizedAccessException("The current Windows user SID is unavailable.");
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            return new FileInfo(path).Create(
                FileMode.CreateNew,
                FileSystemRights.FullControl,
                FileShare.None,
                4096,
                options,
                security);
        }

        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = options,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }

    private sealed record DeletionCandidate(string Id, string PartitionKeyJson);
}
