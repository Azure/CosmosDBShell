//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;

[CosmosCommand("schema")]
[CosmosExample("schema", Description = "Infer the schema of the current container from a small sample")]
[CosmosExample("schema --sample=50", Description = "Sample up to 50 documents when inferring the schema")]
[CosmosExample("schema --database=MyDB --container=Products", Description = "Infer the schema for a specific database and container")]
[McpAnnotation(
    Title = "Schema",
    ReadOnly = true,
    Idempotent = true,
    OpenWorld = true,
    Description = "Returns a cheap, bounded discovery summary of a Cosmos DB container: partition key path(s), an indexing policy summary, an estimated document count, and inferred field types from a bounded sample of documents. Use this before querying to avoid re-sampling and to avoid guessing field names.")]
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

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("schema");
        }

        var (databaseName, containerName, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "schema",
            token);

        int sampleSize = NormalizeSample(this.Sample);

        try
        {
            var settings = await CosmosResourceFacade.GetContainerSettingsAsync(connectedState, databaseName!, containerName!, token);
            var sampledDocuments = await SampleDocumentsAsync(container, sampleSize, token);
            long? documentCountEstimate = await ReadDocumentCountEstimateAsync(container, token);
            var fields = InferSchema(sampledDocuments);

            return BuildResult(databaseName!, containerName!, settings, documentCountEstimate, sampleSize, sampledDocuments.Count, fields);
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
    /// reported using dot notation for nested objects up to <paramref name="maxDepth"/> levels.
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

            CollectFields(document, prefix: string.Empty, depth: 0, maxDepth, fields);
        }

        return fields
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new FieldSchema(pair.Key, pair.Value.Types.ToArray(), pair.Value.Presence))
            .ToList();
    }

    private static void CollectFields(JsonElement element, string prefix, int depth, int maxDepth, Dictionary<string, FieldAccumulator> fields)
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
            accumulator.Presence++;

            if (property.Value.ValueKind == JsonValueKind.Object && depth + 1 < maxDepth)
            {
                CollectFields(property.Value, path, depth + 1, maxDepth, fields);
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
        JsonValueKind.Null => "null",
        _ => "undefined",
    };

    private static async Task<List<JsonElement>> SampleDocumentsAsync(Container container, int sample, CancellationToken token)
    {
        var documents = new List<JsonElement>(sample);
        using var iterator = container.GetItemQueryIterator<JsonElement>(
            new QueryDefinition("SELECT * FROM c"),
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

    private static async Task<long?> ReadDocumentCountEstimateAsync(Container container, CancellationToken token)
    {
        var response = await container.ReadContainerAsync(new ContainerRequestOptions { PopulateQuotaInfo = true }, token);
        return InfoCommand.ParseResourceUsage(response.Headers[ResourceUsageHeader]).DocumentCount;
    }

    private static CommandState BuildResult(
        string databaseName,
        string containerName,
        ContainerSettingsView settings,
        long? documentCountEstimate,
        int sampleSize,
        int sampledDocuments,
        IReadOnlyList<FieldSchema> fields)
    {
        Dictionary<string, object?>? indexingPolicy = settings.IndexingPolicy is { } indexing
            ? new Dictionary<string, object?>
            {
                ["indexingMode"] = indexing.IndexingMode,
                ["automatic"] = indexing.Automatic,
                ["includedPaths"] = indexing.IncludedPathCount,
                ["excludedPaths"] = indexing.ExcludedPathCount,
                ["compositeIndexes"] = indexing.CompositeIndexCount,
                ["spatialIndexes"] = indexing.SpatialIndexCount,
                ["vectorIndexes"] = indexing.VectorIndexCount,
            }
            : null;

        var output = new Dictionary<string, object?>
        {
            ["database"] = databaseName,
            ["container"] = containerName,
            ["partitionKeyPaths"] = settings.PartitionKeyPaths,
            ["documentCountEstimate"] = documentCountEstimate,
            ["sampleSize"] = sampleSize,
            ["sampledDocuments"] = sampledDocuments,
            ["indexingPolicy"] = indexingPolicy,
            ["fields"] = fields.Select(field => new Dictionary<string, object?>
            {
                ["path"] = field.Path,
                ["types"] = field.Types,
                ["presence"] = field.Presence,
            }).ToList(),
        };

        return new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(output)),
        };
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
