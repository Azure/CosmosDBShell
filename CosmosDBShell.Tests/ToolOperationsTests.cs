// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

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
        Assert.Equal(ToolOperations.DefaultPageSize, maxProperty.GetProperty("default").GetInt32());
        Assert.Contains("Aliases: db", databaseProperty.GetProperty("description").GetString());
        Assert.Contains("Aliases: con", containerProperty.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("query")]
    [InlineData("ls")]
    public void GetTool_PagedMaxDescription_DocumentsSinglePageSemanticsWithoutChangingShellHelp(string commandName)
    {
        var factory = new CommandRunner().Commands[commandName];

        var tool = ToolOperations.GetTool(factory);
        var schema = JsonDocument.Parse(tool.InputSchema.GetRawText()).RootElement;
        var maxDescription = schema.GetProperty("properties").GetProperty("max").GetProperty("description").GetString();

        Assert.Contains("continuationToken", maxDescription);

        var shellDescription = factory.Options.Single(option => option.Name[0] == "max").GetDescription(commandName);
        Assert.DoesNotContain("continuationToken", shellDescription);
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

        Assert.Equal(" --database \"Samples\"", formattedOption);
    }

    [Theory]
    [InlineData("has space", " --database \"has space\"")]
    [InlineData("with\"quote", " --database \"with\\\"quote\"")]
    [InlineData("back\\slash", " --database \"back\\\\slash\"")]
    [InlineData("line\nbreak", " --database \"line\\nbreak\"")]
    [InlineData("$name", " --database \"\\$name\"")]
    [InlineData("$(echo injected)", " --database \"\\$(echo injected)\"")]
    [InlineData("foo; echo injected", " --database \"foo; echo injected\"")]
    [InlineData("left|right", " --database \"left|right\"")]
    [InlineData("escape\u001Bsequence", " --database \"escape\\u001Bsequence\"")]
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
    public void ConfigurePaging_MarksQueryAsMcpRequest()
    {
        var command = new QueryCommand();

        ToolOperations.ConfigurePaging(command);

        Assert.Null(command.Max);
        Assert.True(command.IsMcpRequest);
    }

    [Fact]
    public void ConfigurePaging_PreservesExplicitMaximum()
    {
        var command = new ListCommand { Max = 25 };

        ToolOperations.ConfigurePaging(command);

        Assert.Equal(25, command.Max);
        Assert.True(command.IsMcpRequest);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("ls")]
    public void GetTool_ExposesContinuationWithoutMakingItAShellOption(string commandName)
    {
        var factory = new CommandRunner().Commands[commandName];

        Assert.True(factory.IsPaged);
        Assert.DoesNotContain(factory.AllOptions, option => ToolOperations.MatchesArgumentName(option.Name, "continuation"));

        var tool = ToolOperations.GetTool(factory);
        var schema = JsonDocument.Parse(tool.InputSchema.GetRawText()).RootElement;
        var continuation = schema.GetProperty("properties").GetProperty("continuation");

        Assert.Equal("string", continuation.GetProperty("type").GetString());
    }

    [Fact]
    public void GetTool_OmitsContinuationForUnpagedCommands()
    {
        var factory = new CommandRunner().Commands["echo"];

        var tool = ToolOperations.GetTool(factory);
        var schema = JsonDocument.Parse(tool.InputSchema.GetRawText()).RootElement;

        Assert.False(factory.IsPaged);
        if (schema.TryGetProperty("properties", out var properties))
        {
            Assert.False(properties.TryGetProperty("continuation", out _));
        }
    }

    [Fact]
    public void TrySetContinuation_AssignsTokenToPagedCommand()
    {
        var command = new ListCommand();

        Assert.True(ToolOperations.TrySetContinuation(command, "continuation", Json("\"token-1\""), out var error));
        Assert.Null(error);
        Assert.Equal("token-1", command.Continuation);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    public void TrySetContinuation_RejectsNonStringValue(string json)
    {
        var command = new ListCommand();

        Assert.True(ToolOperations.TrySetContinuation(command, "continuation", Json(json), out var error));
        Assert.Equal("Invalid value for MCP argument 'continuation'. Expected a non-null string.", error);
        Assert.Null(command.Continuation);
    }

    [Fact]
    public void TrySetContinuation_IgnoresUnrelatedArguments()
    {
        var command = new ListCommand();

        Assert.False(ToolOperations.TrySetContinuation(command, "max", Json("\"token-1\""), out var error));
        Assert.Null(error);
        Assert.Null(command.Continuation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("$name")]
    [InlineData("$(echo injected)")]
    [InlineData("foo; echo injected")]
    [InlineData("left|right")]
    [InlineData("with\"quote")]
    [InlineData("back\\slash")]
    [InlineData("line\nbreak\tend")]
    [InlineData("escape\u001Bsequence")]
    public void ShellLiteral_Quote_RoundTripsAsConstantExpression(string value)
    {
        var expression = new ExpressionParser(new Lexer(ShellLiteral.Quote(value))).ParseExpression();

        var constant = Assert.IsType<ConstantExpression>(expression);
        var text = Assert.IsType<ShellText>(constant.Value);
        Assert.Equal(value, text.Text);
    }

    [Fact]
    public void GetTool_AppendsUserOnlyWarningForRestrictedCommands()
    {
        var factory = new CommandRunner().Commands["edit"];
        Assert.True(factory.McpRestricted);

        var tool = ToolOperations.GetTool(factory);

        Assert.Contains("cannot be invoked through MCP", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTool_AppendsConfirmationWarningForDestructiveCommands()
    {
        var factory = new CommandRunner().Commands["delete"];
        Assert.True(factory.McpRestricted);

        var tool = ToolOperations.GetTool(factory);

        Assert.Contains("requires explicit user confirmation", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot be invoked through MCP", tool.Description, StringComparison.OrdinalIgnoreCase);
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
    public void GetTool_BatchDescription_OnlyOffersStatelessRunThroughMcp()
    {
        var factory = new CommandRunner().Commands["batch"];

        var tool = ToolOperations.GetTool(factory);

        Assert.Contains("MCP supports only the one-shot 'run' subcommand", tool.Description);
        Assert.Contains("available only in the interactive shell", tool.Description);
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
    public void GetTool_SchemaExposesOptionsAndReadOnlyAnnotations()
    {
        var factory = new CommandRunner().Commands["schema"];

        var tool = ToolOperations.GetTool(factory);
        var properties = tool.InputSchema.GetProperty("properties");

        Assert.Equal("integer", properties.GetProperty("sample").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("database").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("container").GetProperty("type").GetString());
        Assert.Equal("boolean", properties.GetProperty("fields-only").GetProperty("type").GetString());
        Assert.Contains("Aliases: short", properties.GetProperty("fields-only").GetProperty("description").GetString());
        Assert.NotNull(tool.Annotations);
        Assert.Equal("Schema", tool.Annotations!.Title);
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

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }
}
