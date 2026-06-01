// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// Provides resource templates and an MCP <c>completion/complete</c> handler that
/// suggests live database and container names from the connected Cosmos DB
/// account. Reuses <see cref="DataPlaneCosmosResourceOperations"/> — the same
/// discovery code path that <see cref="ResourceOperations"/> uses for static
/// resource resolution — so the two surfaces stay in sync.
/// </summary>
internal static class ResourceCompletionOperations
{
    /// <summary>
    /// Resource template URI for listing containers in a database. Placeholder
    /// <c>{database}</c> is completable.
    /// </summary>
    internal const string DatabaseContainersTemplate = "cosmos://databases/{database}/containers";

    /// <summary>
    /// Resource template URI for a container's indexing policy. Both <c>{database}</c>
    /// and <c>{container}</c> are completable.
    /// </summary>
    internal const string ContainerIndexingPolicyTemplate = "cosmos://databases/{database}/containers/{container}/indexing-policy";

    private const int MaxCompletionValues = 100;

    internal static readonly IReadOnlyList<ResourceTemplate> Templates = new[]
    {
        new ResourceTemplate
        {
            Name = "cosmos-database-containers",
            Title = "Containers in a database",
            UriTemplate = DatabaseContainersTemplate,
            Description = "Containers in the named Cosmos DB database. Completion is available for {database}.",
            MimeType = "application/json",
        },
        new ResourceTemplate
        {
            Name = "cosmos-container-indexing-policy",
            Title = "Container indexing policy",
            UriTemplate = ContainerIndexingPolicyTemplate,
            Description = "Indexing policy of a named container. Completion is available for {database} and {container}.",
            MimeType = "application/json",
        },
    };

    public static ValueTask<ListResourceTemplatesResult> ListResourceTemplatesAsync(
        RequestContext<ListResourceTemplatesRequestParams> context,
        CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;

        var result = new ListResourceTemplatesResult
        {
            ResourceTemplates = Templates.ToList(),
        };
        return new ValueTask<ListResourceTemplatesResult>(result);
    }

    public static ValueTask<CompleteResult> CompleteAsync(
        RequestContext<CompleteRequestParams> context,
        CancellationToken cancellationToken)
    {
        return CompleteAsync(context.Params, cancellationToken);
    }

    internal static async ValueTask<CompleteResult> CompleteAsync(
        CompleteRequestParams? parameters,
        CancellationToken cancellationToken)
    {
        if (!TryGetCompletionTarget(parameters?.Ref))
        {
            return EmptyCompletion();
        }

        var argumentName = parameters!.Argument?.Name;
        var argumentValue = parameters.Argument?.Value ?? string.Empty;
        if (string.IsNullOrEmpty(argumentName))
        {
            return EmptyCompletion();
        }

        if (ShellInterpreter.Instance.State is not ConnectedState connectedState)
        {
            return EmptyCompletion();
        }

        try
        {
            return argumentName switch
            {
                "database" => await SuggestDatabasesAsync(connectedState.Client, argumentValue, cancellationToken).ConfigureAwait(false),
                "container" => await SuggestContainersAsync(connectedState.Client, parameters.Context?.Arguments, argumentValue, cancellationToken).ConfigureAwait(false),
                _ => EmptyCompletion(),
            };
        }
        catch (CosmosException)
        {
            return EmptyCompletion();
        }
    }

    private static bool IsKnownTemplate(string uri)
    {
        return string.Equals(uri, DatabaseContainersTemplate, StringComparison.Ordinal)
            || string.Equals(uri, ContainerIndexingPolicyTemplate, StringComparison.Ordinal);
    }

    private static bool TryGetCompletionTarget(Reference? reference)
    {
        return reference is ResourceTemplateReference templateRef
            && !string.IsNullOrEmpty(templateRef.Uri)
            && IsKnownTemplate(templateRef.Uri);
    }

    private static async Task<CompleteResult> SuggestDatabasesAsync(
        CosmosClient client,
        string prefix,
        CancellationToken cancellationToken)
    {
        var ops = new DataPlaneCosmosResourceOperations(client);
        var matches = new List<string>();
        var total = 0;

        await foreach (var name in ops.GetDatabaseNamesAsync(cancellationToken))
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total++;
            if (matches.Count < MaxCompletionValues)
            {
                matches.Add(name);
            }
        }

        return BuildCompletion(matches, total);
    }

    private static async Task<CompleteResult> SuggestContainersAsync(
        CosmosClient client,
        IDictionary<string, string>? contextArguments,
        string prefix,
        CancellationToken cancellationToken)
    {
        if (contextArguments == null ||
            !contextArguments.TryGetValue("database", out var database) ||
            string.IsNullOrWhiteSpace(database))
        {
            return EmptyCompletion();
        }

        var ops = new DataPlaneCosmosResourceOperations(client);
        var matches = new List<string>();
        var total = 0;

        await foreach (var name in ops.GetContainerNamesAsync(database, cancellationToken))
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total++;
            if (matches.Count < MaxCompletionValues)
            {
                matches.Add(name);
            }
        }

        return BuildCompletion(matches, total);
    }

    private static CompleteResult BuildCompletion(IList<string> values, int total)
    {
        return new CompleteResult
        {
            Completion = new Completion
            {
                Values = values,
                Total = total,
                HasMore = total > values.Count,
            },
        };
    }

    private static CompleteResult EmptyCompletion()
    {
        return new CompleteResult
        {
            Completion = new Completion
            {
                Values = Array.Empty<string>(),
                Total = 0,
                HasMore = false,
            },
        };
    }
}
