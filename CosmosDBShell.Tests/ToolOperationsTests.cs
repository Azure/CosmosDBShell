// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;

public class ToolOperationsTests
{
    [Fact]
    public void GetTool_IncludesCommandOptionsInInputSchema()
    {
        var factory = new CommandRunner().Commands["query"];

        var tool = ToolOperations.GetTool(factory);
        var schema = JsonDocument.Parse(tool.InputSchema.GetRawText()).RootElement;
        var properties = schema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("query", out var queryProperty));
        Assert.True(properties.TryGetProperty("database", out var databaseProperty));
        Assert.True(properties.TryGetProperty("container", out var containerProperty));
        Assert.True(properties.TryGetProperty("max", out var maxProperty));
        Assert.Equal("string", queryProperty.GetProperty("type").GetString());
        Assert.Equal("string", databaseProperty.GetProperty("type").GetString());
        Assert.Equal("string", containerProperty.GetProperty("type").GetString());
        Assert.Equal("integer", maxProperty.GetProperty("type").GetString());
        Assert.Contains("Aliases: db", databaseProperty.GetProperty("description").GetString());
        Assert.Contains("Aliases: con", containerProperty.GetProperty("description").GetString());
    }

    [Fact]
    public void GetTool_MarksRequiredParametersWithoutRequiringOptions()
    {
        var factory = new CommandRunner().Commands["query"];

        var tool = ToolOperations.GetTool(factory);
        var schema = JsonDocument.Parse(tool.InputSchema.GetRawText()).RootElement;
        var required = schema.GetProperty("required").EnumerateArray().Select(element => element.GetString()).ToArray();

        Assert.Contains("query", required);
        Assert.DoesNotContain("database", required);
        Assert.DoesNotContain("container", required);
        Assert.DoesNotContain("max", required);
    }

    [Fact]
    public void MatchesArgumentName_AcceptsAliasesCaseInsensitively()
    {
        var factory = new CommandRunner().Commands["query"];
        var databaseOption = factory.Options.Single(option => option.Name[0] == "database");

        Assert.True(ToolOperations.MatchesArgumentName(databaseOption.Name, "db"));
        Assert.True(ToolOperations.MatchesArgumentName(databaseOption.Name, "DB"));
        Assert.True(ToolOperations.MatchesArgumentName(databaseOption.Name, "database"));
    }
}