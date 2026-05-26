// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Mcp;

using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// Server-defined MCP prompt templates that MCP clients (e.g. Claude Desktop,
/// VS Code MCP UI) surface as slash-commands. Each prompt produces a short
/// message sequence that steers the model toward known-good workflows using
/// the shell's tools and resources.
/// </summary>
[McpServerPromptType]
internal class PromptOperations
{
    [McpServerPrompt(
        Name = "cosmos.explain-container",
        Title = "Explain a Cosmos DB container")]
    [Description("Walk the assistant through reading the indexing policy and a sample of items in a container and producing a structured summary.")]
    public static GetPromptResult ExplainContainer(
        [Description("Database name.")] string database,
        [Description("Container name.")] string container)
    {
        var text = $$"""
            Analyze the Cosmos DB container `{{database}}/{{container}}` and produce a structured summary.

            Steps:
            1. Read the resource `cosmos://databases/{{database}}/containers/{{container}}/indexing-policy`.
            2. Invoke the `query` tool with arguments: `{ "query": "SELECT TOP 5 * FROM c", "database": "{{database}}", "container": "{{container}}" }`.
            3. Inspect the partition-key path, indexing policy, and the shape of the sampled documents.

            Output JSON with this shape:
            {
              "partitionKey": "<path>",
              "indexingSummary": "<one paragraph>",
              "sampleShape": { /* representative document shape */ },
              "concerns": [ /* zero or more short strings */ ]
            }
            """;

        return UserPrompt($"Explain {database}/{container}", text);
    }

    [McpServerPrompt(
        Name = "cosmos.query-optimize",
        Title = "Optimize a Cosmos DB NoSQL query")]
    [Description("Profile a NoSQL query, inspect the container's indexing policy, and propose a rewrite.")]
    public static GetPromptResult QueryOptimize(
        [Description("The NoSQL query to analyze and optimize.")] string query,
        [Description("Database name. Optional when the shell is already scoped to one via cd.")] string? database = null,
        [Description("Container name. Optional when the shell is already scoped to one via cd.")] string? container = null)
    {
        var scope = BuildScopeArgs(database, container);
        var indexingResourceHint = database != null && container != null
            ? $"`cosmos://databases/{database}/containers/{container}/indexing-policy`"
            : "`cosmos://current/container/indexing-policy`";

        var text = $$"""
            Optimize the following Cosmos DB NoSQL query:

            ```sql
            {{query}}
            ```

            Steps:
            1. Run the `query` tool with arguments: `{ "query": "{{EscapeJson(query)}}"{{scope}} }` and observe the RU charge plus row count.
            2. Read the resource {{indexingResourceHint}} to understand the indexing strategy.
            3. Propose a rewrite that reduces RU/s, leveraging existing indexes where possible. If a different index would help, call it out explicitly.

            Output:
            - A unified diff (original → proposed) inside a ```diff fenced block.
            - A one-line justification.
            - If no rewrite improves cost, state that and explain why.
            """;

        return UserPrompt("Optimize Cosmos DB query", text);
    }

    [McpServerPrompt(
        Name = "cosmos.partition-key-audit",
        Title = "Audit a container's partition-key choice")]
    [Description("Inspect the indexing policy and a sample of items to evaluate the current partition key.")]
    public static GetPromptResult PartitionKeyAudit(
        [Description("Database name.")] string database,
        [Description("Container name.")] string container)
    {
        var text = $$"""
            Audit the partition-key choice for `{{database}}/{{container}}`.

            Steps:
            1. Read `cosmos://databases/{{database}}/containers/{{container}}/indexing-policy` to capture the current partition-key path.
            2. Run the `query` tool: `{ "query": "SELECT TOP 50 * FROM c", "database": "{{database}}", "container": "{{container}}" }`.
            3. Infer the likely access patterns from the sampled documents.
            4. Evaluate the current partition key against those access patterns: cardinality, hot-partition risk, cross-partition query frequency.

            Output JSON:
            {
              "currentPartitionKey": "<path>",
              "verdict": "good" | "acceptable" | "risky",
              "rationale": "<one paragraph>",
              "suggestedPartitionKey": "<path or null>",
              "migrationNotes": "<short string or null>"
            }
            """;

        return UserPrompt($"Audit partition key of {database}/{container}", text);
    }

    [McpServerPrompt(
        Name = "cosmos.bulk-import-plan",
        Title = "Plan a bulk-import into a Cosmos DB container")]
    [Description("Produce a concrete, batched, retry-aware plan for importing items from a file into a container.")]
    public static GetPromptResult BulkImportPlan(
        [Description("Path to the source file (JSON, JSONL, or CSV).")] string file,
        [Description("Target database name.")] string database,
        [Description("Target container name.")] string container)
    {
        var text = $$"""
            Plan a bulk-import of `{{file}}` into Cosmos DB container `{{database}}/{{container}}`.

            Use the `mkitem` and `create` tools. Constraints:
            - Validate the file format and a single record first (dry-run style).
            - Batch writes (recommend a batch size given the document size).
            - Handle 429 (rate-limit) responses with exponential backoff.
            - Surface progress every N batches.
            - Stop on schema mismatch and report the offending record.

            Output a numbered execution plan. Each step must list the exact tool call (name + arguments) the assistant intends to make. End with a one-line rollback hint.
            """;

        return UserPrompt($"Bulk-import {file} into {database}/{container}", text);
    }

    [McpServerPrompt(
        Name = "cosmos.connect-help",
        Title = "Help me connect to Cosmos DB")]
    [Description("Walk the user through choosing the right connect command for their environment.")]
    public static GetPromptResult ConnectHelp(
        [Description("Optional account endpoint URL (e.g. https://my-account.documents.azure.com:443/).")] string? endpoint = null)
    {
        var endpointHint = endpoint != null
            ? $"\nThe user mentioned this endpoint: `{endpoint}`."
            : string.Empty;

        var text = $$"""
            The user wants to connect to a Cosmos DB account using Cosmos Shell.{{endpointHint}}

            Reference material:
            - `cosmos://docs/commands` — JSON catalog of every available command.
            - `cosmos://docs/scripting` — full scripting reference.

            Help the user choose between:
            1. Entra ID (recommended for production). Requires `connect`, optionally `--connect-subscription` / `--connect-resource-group` for ARM routing.
            2. Account key / connection string (legacy, key rotation risk).
            3. Emulator (local dev, self-signed cert flag).

            Ask one or two short questions to pin down the scenario, then output:
            - The exact `connect` command to run.
            - The minimum Azure RBAC role they need.
            - One follow-up command (e.g. `ls` or `pwd`) to verify the connection.
            """;

        return UserPrompt("Help me connect to Cosmos DB", text);
    }

    private static GetPromptResult UserPrompt(string description, string text)
    {
        return new GetPromptResult
        {
            Description = description,
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = text,
                    },
                },
            ],
        };
    }

    private static string BuildScopeArgs(string? database, string? container)
    {
        var builder = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.Append(", \"database\": \"").Append(database).Append('"');
        }

        if (!string.IsNullOrWhiteSpace(container))
        {
            builder.Append(", \"container\": \"").Append(container).Append('"');
        }

        return builder.ToString();
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
