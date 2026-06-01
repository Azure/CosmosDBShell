// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Reflection;
using Azure.Data.Cosmos.Shell.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public class PromptOperationsTests
{
    [Fact]
    public void ExplainContainer_ProducesUserMessageReferencingArgs()
    {
        var result = PromptOperations.ExplainContainer("Sales", "Orders");

        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        var text = Assert.IsType<TextContentBlock>(message.Content).Text;
        Assert.Contains("Sales/Orders", text);
        Assert.Contains("cosmos://databases/Sales/containers/Orders/indexing-policy", text);
        Assert.Contains("query", text);
    }

    [Fact]
    public void QueryOptimize_InlinesQueryAndScope()
    {
        var result = PromptOperations.QueryOptimize("SELECT * FROM c WHERE c.id = '1'", "Sales", "Orders");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.Contains("SELECT * FROM c WHERE c.id = '1'", text);
        Assert.Contains("\"database\": \"Sales\"", text);
        Assert.Contains("\"container\": \"Orders\"", text);
        Assert.Contains("cosmos://databases/Sales/containers/Orders/indexing-policy", text);
    }

    [Fact]
    public void QueryOptimize_WithoutScope_FallsBackToCurrentResource()
    {
        var result = PromptOperations.QueryOptimize("SELECT 1");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.Contains("cosmos://current/container/indexing-policy", text);
        Assert.DoesNotContain("\"database\":", text);
    }

    [Fact]
    public void PartitionKeyAudit_ProducesUserMessage()
    {
        var result = PromptOperations.PartitionKeyAudit("Sales", "Orders");

        Assert.NotNull(result.Description);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.Contains("partition-key", text);
        Assert.Contains("Sales/Orders", text);
    }

    [Fact]
    public void BulkImportPlan_IncludesFilePathAndTarget()
    {
        var result = PromptOperations.BulkImportPlan("C:/data/items.json", "Sales", "Orders");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.Contains("C:/data/items.json", text);
        Assert.Contains("Sales/Orders", text);
        Assert.Contains("mkitem", text);
    }

    [Fact]
    public void ConnectHelp_WithoutEndpoint_OmitsEndpointHint()
    {
        var result = PromptOperations.ConnectHelp();

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.DoesNotContain("The user mentioned this endpoint", text);
    }

    [Fact]
    public void ConnectHelp_WithEndpoint_IncludesEndpointHint()
    {
        var result = PromptOperations.ConnectHelp("https://x.documents.azure.com:443/");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.Contains("https://x.documents.azure.com:443/", text);
    }

    [Fact]
    public void QueryOptimize_EscapesQuotesAndBackslashesInScope()
    {
        var result = PromptOperations.QueryOptimize("SELECT 1", "He said \"hi\"", "C:\\path");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.Contains("\"database\": \"He said \\\"hi\\\"\"", text);
        Assert.Contains("\"container\": \"C:\\\\path\"", text);
    }

    [Fact]
    public void QueryOptimize_EscapesQueryWithEmbeddedQuotes()
    {
        var result = PromptOperations.QueryOptimize("SELECT \"id\" FROM c");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.Contains("\"query\": \"SELECT \\\"id\\\" FROM c\"", text);
    }

    [Fact]
    public void AllPrompts_DeclareNameAndTitle()
    {
        var methods = typeof(PromptOperations).GetMethods(BindingFlags.Public | BindingFlags.Static);
        var promptMethods = methods
            .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() != null)
            .ToArray();

        Assert.NotEmpty(promptMethods);
        foreach (var method in promptMethods)
        {
            var attr = method.GetCustomAttribute<McpServerPromptAttribute>()!;
            Assert.False(string.IsNullOrWhiteSpace(attr.Name), $"Prompt {method.Name} missing Name.");
            Assert.False(string.IsNullOrWhiteSpace(attr.Title), $"Prompt {method.Name} missing Title.");
            Assert.StartsWith("cosmos.", attr.Name);
        }
    }
}
