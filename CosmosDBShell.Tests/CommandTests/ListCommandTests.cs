// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class ListCommandTests
{
    [Fact]
    public void GetPartitionKeyPropertyNames_ReturnsAllHierarchicalPaths()
    {
        var paths = CosmosCommand.GetPartitionKeyPropertyNames(["/tenantId", "/userId", "/sessionId"]);

        Assert.Equal(["tenantId", "userId", "sessionId"], paths);
    }

    [Fact]
    public void MatchesAnyPath_MatchesAnyHierarchicalPartitionKeyPath()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenantId": "tenant-a",
          "userId": "user-b",
          "sessionId": "session-c"
        }
        """);

        var isMatch = CosmosCommand.MatchesAnyPath(
            document.RootElement,
            ["tenantId", "userId", "sessionId"],
            new PatternMatcher("user-b"));

        Assert.True(isMatch);
    }

    [Fact]
    public void MatchesAnyPath_SupportsNestedPartitionKeyPaths()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenant": {
            "id": "tenant-a"
          },
          "user": {
            "id": "user-b"
          }
        }
        """);

        var isMatch = CosmosCommand.MatchesAnyPath(
            document.RootElement,
            ["tenant/id", "user/id"],
            new PatternMatcher("tenant-a"));

        Assert.True(isMatch);
    }

    [Fact]
    public void MatchesAnyPath_ReturnsFalseWhenNoPartitionKeyPathMatches()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenantId": "tenant-a",
          "userId": "user-b"
        }
        """);

        var isMatch = CosmosCommand.MatchesAnyPath(
            document.RootElement,
            ["tenantId", "userId"],
            new PatternMatcher("session-c"));

        Assert.False(isMatch);
    }

    [Fact]
    public async Task ReadQueryResponseAsync_NullContent_ThrowsFriendlyCommandException()
    {
        using var response = new ResponseMessage(HttpStatusCode.OK)
        {
            Content = null,
        };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => ListCommand.ReadQueryResponseAsync(response, CancellationToken.None));

        Assert.Equal("ls", exception.Command);
        Assert.Contains("no response body", exception.Message);
        Assert.Contains("not an empty-container result", exception.Message);
        Assert.DoesNotContain("Value cannot be null", exception.Message);
    }

    [Fact]
    public async Task ReadQueryResponseAsync_EmptyContent_ThrowsFriendlyCommandException()
    {
        using var response = new ResponseMessage(HttpStatusCode.OK)
        {
            Content = new MemoryStream([]),
        };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => ListCommand.ReadQueryResponseAsync(response, CancellationToken.None));

        Assert.Equal("ls", exception.Command);
        Assert.Contains("empty response body", exception.Message);
        Assert.Contains("not an empty-container result", exception.Message);
    }

    [Fact]
    public async Task ReadQueryResponseAsync_ErrorWithoutContent_ThrowsStatusMessage()
    {
        using var response = new ResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = null,
        };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => ListCommand.ReadQueryResponseAsync(response, CancellationToken.None));

        Assert.Equal("ls", exception.Command);
        Assert.Contains("429", exception.Message);
    }

    [Fact]
    public async Task ReadQueryResponseAsync_ValidContent_ReturnsJsonDocument()
    {
        var content = Encoding.UTF8.GetBytes("{\"Documents\":[{\"id\":\"1\"}]}");
        using var response = new ResponseMessage(HttpStatusCode.OK)
        {
            Content = new MemoryStream(content),
        };

        using var document = await ListCommand.ReadQueryResponseAsync(response, CancellationToken.None);

        var item = Assert.Single(document.RootElement.GetProperty("Documents").EnumerateArray());
        Assert.Equal("1", item.GetProperty("id").GetString());
    }
}
