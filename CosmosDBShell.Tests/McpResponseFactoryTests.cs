// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using ModelContextProtocol.Protocol;

namespace CosmosShell.Tests;

public class McpResponseFactoryTests
{
    [Fact]
    public void CreateSuccess_WrapsResultWithCurrentLocation()
    {
        var commandState = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                connected = true,
            })),
        };

        var result = McpResponseFactory.CreateSuccess(commandState, new ContainerState("TestContainer", "TestDatabase", null!));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        using var document = JsonDocument.Parse(text);
        Assert.False(result.IsError);
        Assert.Equal("/TestDatabase/TestContainer", document.RootElement.GetProperty("currentLocation").GetString());
        Assert.True(document.RootElement.GetProperty("result").GetProperty("connected").GetBoolean());
    }

    [Fact]
    public void CreateSuccess_IncludesCsvOutputText()
    {
        var commandState = new CommandState
        {
            OutputFormat = OutputFormat.CSV,
            Result = new ShellJson(JsonSerializer.SerializeToElement(new[]
            {
                new { id = "1", name = "alpha" },
            })),
        };

        var result = McpResponseFactory.CreateSuccess(commandState, new ConnectedState(null!));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        using var document = JsonDocument.Parse(text);
        Assert.Equal("/", document.RootElement.GetProperty("currentLocation").GetString());
        Assert.True(document.RootElement.TryGetProperty("outputText", out var outputText));
        Assert.Contains("alpha", outputText.GetString());
    }

    [Fact]
    public void CreateSuccess_WrapsTextResultAsJsonString()
    {
        var commandState = new CommandState
        {
            Result = new ShellText("plain text"),
        };

        var result = McpResponseFactory.CreateSuccess(commandState, new ConnectedState(null!));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        using var document = JsonDocument.Parse(text);
        Assert.False(result.IsError);
        Assert.Equal("plain text", document.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public void CreateSuccess_WhenErrorMessageMissing_UsesFallbackError()
    {
        var result = McpResponseFactory.CreateSuccess(new ErrorCommandState(new Exception(string.Empty)), new ConnectedState(null!));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        using var document = JsonDocument.Parse(text);
        Assert.True(result.IsError);
        Assert.Equal("Command execution failed.", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void CreateError_WrapsMessageWithCurrentLocation()
    {
        var result = McpResponseFactory.CreateError("boom", new DatabaseState("TestDatabase", null!));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        using var document = JsonDocument.Parse(text);
        Assert.True(result.IsError);
        Assert.Equal("/TestDatabase", document.RootElement.GetProperty("currentLocation").GetString());
        Assert.Equal("boom", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void CreateSuccess_WhenDisconnected_UsesNullCurrentLocation()
    {
        var result = McpResponseFactory.CreateSuccess(new CommandState(), new DisconnectedState());
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        using var document = JsonDocument.Parse(text);
        Assert.False(result.IsError);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("currentLocation").ValueKind);
    }

    [Fact]
    public void CreateSuccess_PopulatesStructuredContentMatchingTextPayload()
    {
        var commandState = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new
            {
                connected = true,
            })),
        };

        var result = McpResponseFactory.CreateSuccess(commandState, new ContainerState("c", "db", null!));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        Assert.NotNull(result.StructuredContent);
        var structured = result.StructuredContent!.Value;
        Assert.Equal(JsonValueKind.Object, structured.ValueKind);
        Assert.Equal("/db/c", structured.GetProperty("currentLocation").GetString());
        Assert.True(structured.GetProperty("result").GetProperty("connected").GetBoolean());

        using var fromText = JsonDocument.Parse(text);
        Assert.Equal(
            fromText.RootElement.GetProperty("result").GetProperty("connected").GetBoolean(),
            structured.GetProperty("result").GetProperty("connected").GetBoolean());
    }

    [Fact]
    public void CreateError_PopulatesStructuredContent()
    {
        var result = McpResponseFactory.CreateError("boom", new DisconnectedState());

        Assert.NotNull(result.StructuredContent);
        var structured = result.StructuredContent!.Value;
        Assert.Equal("boom", structured.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("currentLocation").ValueKind);
    }
}