//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("schema")]
[CosmosExample("schema", Description = "Infer the schema of the current container from a small sample")]
[CosmosExample("schema --sample=50", Description = "Sample up to 50 documents when inferring the schema")]
[CosmosExample("schema --fields-only", Description = "Return only the inferred fields and sampled document count")]
[CosmosExample("schema --database=MyDB --container=Products", Description = "Infer the schema for a specific database and container")]
[McpAnnotation(
    Title = "Schema",
    ReadOnly = true,
    Idempotent = true,
    OpenWorld = true,
    Description = "Returns a cheap, bounded discovery summary of a Cosmos DB container: partition key path(s), an indexing policy summary, an estimated document count, and inferred field types from a bounded sample of documents. Use fields-only (alias: short) to return only sampledDocuments and fields without reading container metadata. Use this before querying to avoid re-sampling and to avoid guessing field names.")]
internal class SchemaCommand : CosmosCommand
{
    internal const int DefaultSample = 20;
    internal const int MinSample = 1;
    internal const int MaxSample = 100;

    private const int DefaultMaxDepth = 8;
    private const string ResourceUsageHeader = "x-ms-resource-usage";

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    [CosmosOption("sample", "s")]
    public int? Sample { get; init; }

    [CosmosOption("fields-only", "short")]
    public bool FieldsOnly { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("schema");
        }

        var (databaseName, containerName, container) = ResolveContainerReference(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "schema");

        int sampleSize = NormalizeSample(this.Sample);

        try
        {
            ContainerResponse? containerResponse = null;
            if (!this.FieldsOnly)
            {
                containerResponse = await container.ReadContainerAsync(new ContainerRequestOptions { PopulateQuotaInfo = true }, token);
            }

            var sampledDocuments = await SampleDocumentsAsync(container, sampleSize, token);
            var fields = InferSchema(sampledDocuments);
            if (this.FieldsOnly)
            {
                return BuildFieldsOnlyResult(sampledDocuments.Count, fields);
            }

            long? documentCountEstimate = InfoCommand.ParseResourceUsage(containerResponse!.Headers[ResourceUsageHeader]).DocumentCount;

            return BuildResult(databaseName, containerName, containerResponse.Resource, documentCountEstimate, sampleSize, sampledDocuments.Count, fields);
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CommandException(
                "schema",
                MessageService.GetArgsString(
                    "error-container_not_found",
                    "container",
                    containerName,
                    "database",
                    databaseName),
                e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new CommandException("schema", e);
        }
    }

    /// <summary>
    /// Clamps the requested sample size into the supported <see cref="MinSample"/>..<see cref="MaxSample"/>
    /// range so the discovery query stays bounded regardless of the value supplied.
    /// </summary>
    internal static int NormalizeSample(int? sample)
    {
        if (!sample.HasValue)
        {
            return DefaultSample;
        }

        return Math.Clamp(sample.Value, MinSample, MaxSample);
    }

    /// <summary>
    /// Infers a per-field type summary from a bounded set of sampled documents. Fields are
    /// reported using dot notation with at most <paramref name="maxDepth"/> path segments.
    /// Each field lists the distinct JSON types observed and the number of sampled documents in
    /// which the field was present.
    /// </summary>
    internal static IReadOnlyList<FieldSchema> InferSchema(IReadOnlyList<JsonElement> documents, int maxDepth = DefaultMaxDepth)
    {
        var fields = new Dictionary<string, FieldAccumulator>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            if (document.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            CollectFields(document, prefix: string.Empty, depth: 0, maxDepth, fields, new HashSet<string>(StringComparer.Ordinal));
        }

        return fields
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new FieldSchema(pair.Key, pair.Value.Types.ToArray(), pair.Value.Presence))
            .ToList();
    }

    private static void CollectFields(
        JsonElement element,
        string prefix,
        int depth,
        int maxDepth,
        Dictionary<string, FieldAccumulator> fields,
        HashSet<string> fieldsSeenInDocument)
    {
        foreach (var property in element.EnumerateObject())
        {
            string path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            if (!fields.TryGetValue(path, out var accumulator))
            {
                accumulator = new FieldAccumulator();
                fields[path] = accumulator;
            }

            accumulator.Types.Add(DescribeValueKind(property.Value.ValueKind));
            if (fieldsSeenInDocument.Add(path))
            {
                accumulator.Presence++;
            }

            if (property.Value.ValueKind == JsonValueKind.Object && depth + 1 < maxDepth)
            {
                CollectFields(property.Value, path, depth + 1, maxDepth, fields, fieldsSeenInDocument);
            }
        }
    }

    private static string DescribeValueKind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        _ => "null",
    };

    private static async Task<List<JsonElement>> SampleDocumentsAsync(Container container, int sample, CancellationToken token)
    {
        var documents = new List<JsonElement>(sample);
        using var iterator = container.GetItemQueryIterator<JsonElement>(
            new QueryDefinition(BuildSampleQueryText(sample)),
            requestOptions: new QueryRequestOptions { MaxItemCount = sample });

        while (iterator.HasMoreResults && documents.Count < sample)
        {
            foreach (var element in await iterator.ReadNextAsync(token))
            {
                documents.Add(element.Clone());
                if (documents.Count >= sample)
                {
                    break;
                }
            }
        }

        return documents;
    }

    internal static string BuildSampleQueryText(int sample) => FormattableString.Invariant($"SELECT TOP {sample} * FROM c");

    internal static CommandState BuildFieldsOnlyResult(int sampledDocuments, IReadOnlyList<FieldSchema> fields)
    {
        var output = new Dictionary<string, object?>
        {
            ["sampledDocuments"] = sampledDocuments,
            ["fields"] = BuildFieldsOutput(fields),
        };

        return new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(output)),
        };
    }

    internal static CommandState BuildResult(
        string databaseName,
        string containerName,
        ContainerProperties properties,
        long? documentCountEstimate,
        int sampleSize,
        int sampledDocuments,
        IReadOnlyList<FieldSchema> fields)
    {
        Dictionary<string, object?>? indexingPolicy = properties.IndexingPolicy is { } indexing
            ? new Dictionary<string, object?>
            {
                ["indexingMode"] = indexing.IndexingMode.ToString(),
                ["automatic"] = indexing.Automatic,
                ["includedPaths"] = indexing.IncludedPaths?.Count ?? 0,
                ["excludedPaths"] = indexing.ExcludedPaths?.Count ?? 0,
                ["compositeIndexes"] = indexing.CompositeIndexes?.Count ?? 0,
                ["spatialIndexes"] = indexing.SpatialIndexes?.Count ?? 0,
                ["vectorIndexes"] = indexing.VectorIndexes?.Count ?? 0,
            }
            : null;

        IReadOnlyList<string> partitionKeyPaths = properties.PartitionKeyPaths?.ToArray()
            ?? (properties.PartitionKeyPath != null ? [properties.PartitionKeyPath] : []);

        var output = new Dictionary<string, object?>
        {
            ["database"] = databaseName,
            ["container"] = containerName,
            ["partitionKeyPaths"] = partitionKeyPaths,
            ["documentCountEstimate"] = documentCountEstimate,
            ["sampleSize"] = sampleSize,
            ["sampledDocuments"] = sampledDocuments,
            ["indexingPolicy"] = indexingPolicy,
            ["fields"] = BuildFieldsOutput(fields),
        };

        return new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(output)),
        };
    }

    private static List<Dictionary<string, object?>> BuildFieldsOutput(IReadOnlyList<FieldSchema> fields)
    {
        return fields.Select(field => new Dictionary<string, object?>
        {
            ["path"] = field.Path,
            ["types"] = field.Types,
            ["presence"] = field.Presence,
        }).ToList();
    }

    /// <summary>
    /// The inferred type summary for a single field discovered while sampling a container.
    /// </summary>
    internal sealed record FieldSchema(string Path, IReadOnlyList<string> Types, int Presence);

    private sealed class FieldAccumulator
    {
        public SortedSet<string> Types { get; } = new(StringComparer.Ordinal);

        public int Presence { get; set; }
    }
}
