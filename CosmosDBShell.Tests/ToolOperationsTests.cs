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

    [Fact]
    public void FormatOptionForHistory_UsesDoubleDashPrefix()
    {
        var factory = new CommandRunner().Commands["query"];
        var databaseOption = factory.Options.Single(option => option.Name[0] == "database");

        var formattedOption = ToolOperations.FormatOptionForHistory(databaseOption, "Samples");

        Assert.Equal(" --database Samples", formattedOption);
    }

    [Theory]
    [InlineData("has space", " --database \"has space\"")]
    [InlineData("with\"quote", " --database \"with\\\"quote\"")]
    [InlineData("back\\slash", " --database \"back\\\\slash\"")]
    [InlineData("line\nbreak", " --database \"line\\nbreak\"")]
    public void FormatOptionForHistory_QuotesAndEscapesSpecialValues(string value, string expected)
    {
        var factory = new CommandRunner().Commands["query"];
        var databaseOption = factory.Options.Single(option => option.Name[0] == "database");

        var formattedOption = ToolOperations.FormatOptionForHistory(databaseOption, value);

        Assert.Equal(expected, formattedOption);
    }

    [Fact]
    public void FormatOptionForHistory_RendersNullValueAsEmptyQuotedString()
    {
        var factory = new CommandRunner().Commands["query"];
        var databaseOption = factory.Options.Single(option => option.Name[0] == "database");

        var formattedOption = ToolOperations.FormatOptionForHistory(databaseOption, null);

        Assert.Equal(" --database \"\"", formattedOption);
    }

    [Fact]
    public void GetTool_AppendsUserOnlyWarningForRestrictedCommands()
    {
        var factory = new CommandRunner().Commands["delete"];
        Assert.True(factory.McpRestricted);

        var tool = ToolOperations.GetTool(factory);

        Assert.Contains("cannot be invoked through MCP", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTool_MarksStoredProceduresRestrictedForMcp()
    {
        var factory = new CommandRunner().Commands["sproc"];

        Assert.True(factory.McpRestricted);

        var tool = ToolOperations.GetTool(factory);

        Assert.Contains("cannot be invoked through MCP", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(tool.Annotations);
        Assert.True(tool.Annotations!.DestructiveHint);
        Assert.True(tool.Annotations.OpenWorldHint);
    }

    [Fact]
    public void GetTool_DoesNotAppendWarningForUnrestrictedCommands()
    {
        var factory = new CommandRunner().Commands["query"];
        Assert.False(factory.McpRestricted);

        var tool = ToolOperations.GetTool(factory);

        Assert.DoesNotContain("cannot be invoked through MCP", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTool_MapsReadOnlyAnnotationHints()
    {
        var factory = new CommandRunner().Commands["query"];

        var tool = ToolOperations.GetTool(factory);

        Assert.NotNull(tool.Annotations);
        Assert.Equal("Run Query", tool.Annotations!.Title);
        Assert.True(tool.Annotations.ReadOnlyHint);
        Assert.True(tool.Annotations.IdempotentHint);
        Assert.True(tool.Annotations.OpenWorldHint);
        Assert.NotEqual(true, tool.Annotations.DestructiveHint);
    }

    [Fact]
    public void GetTool_MapsDestructiveAnnotationHint()
    {
        var factory = new CommandRunner().Commands["delete"];

        var tool = ToolOperations.GetTool(factory);

        Assert.NotNull(tool.Annotations);
        Assert.True(tool.Annotations!.DestructiveHint);
    }

    [Fact]
    public void GetTool_RendersEnumOptionAsStringSchemaWithValues()
    {
        var factory = new CommandRunner().Commands["query"];

        var tool = ToolOperations.GetTool(factory);
        var schema = JsonDocument.Parse(tool.InputSchema.GetRawText()).RootElement;
        var metrics = schema.GetProperty("properties").GetProperty("metrics");

        Assert.Equal("string", metrics.GetProperty("type").GetString());
        var enumValues = metrics.GetProperty("enum").EnumerateArray().Select(element => element.GetString()).ToArray();
        Assert.Contains("Display", enumValues);
        Assert.Contains("File", enumValues);
        Assert.Equal("Display", metrics.GetProperty("default").GetString());
    }
}
