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

/// <summary>
/// Unit tests for <see cref="ListCommand"/>. Covers <see cref="ListCommand.BuildItemQueryText"/>
/// (which pushes the per-request limit down to the server with <c>SELECT TOP n</c>
/// when no client-side filter is in play), the hierarchical-partition-key
/// helpers on <see cref="CosmosCommand"/>, and the <c>ls</c>-specific
/// <see cref="ListCommand.ReadQueryResponseAsync"/> error mapping.
/// </summary>
public class ListCommandTests
{
    [Fact]
    public void BuildItemQueryText_NoLimit_NoFilter_UsesUnbounded()
    {
        Assert.Equal("SELECT * FROM c", ListCommand.BuildItemQueryText(null, null));
    }

    [Fact]
    public void BuildItemQueryText_WithLimit_NoFilter_UsesTop()
    {
        Assert.Equal("SELECT TOP 25 * FROM c", ListCommand.BuildItemQueryText(25, null));
    }

    [Fact]
    public void BuildItemQueryText_WithLimit_WildcardFilter_UsesTop()
    {
        // '*' is treated by ListCommand as "no filtering", so it is safe to
        // push the cap to the server.
        Assert.Equal("SELECT TOP 10 * FROM c", ListCommand.BuildItemQueryText(10, "*"));
    }

    [Fact]
    public void BuildItemQueryText_WithLimit_SubstringFilter_StaysUnbounded()
    {
        // A substring filter is applied in the shell against the partition or
        // custom key, so capping server-side rows would silently drop matching
        // items. Keep paging client-side.
        Assert.Equal("SELECT * FROM c", ListCommand.BuildItemQueryText(10, "active"));
    }

    [Fact]
    public void BuildItemQueryText_NoLimit_SubstringFilter_StaysUnbounded()
    {
        Assert.Equal("SELECT * FROM c", ListCommand.BuildItemQueryText(null, "active"));
    }

    [Fact]
    public void BuildItemQueryText_EmptyFilter_TreatedAsNoFilter()
    {
        Assert.Equal("SELECT TOP 5 * FROM c", ListCommand.BuildItemQueryText(5, string.Empty));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("*", false)]
    [InlineData("active", true)]
    public void HasClientSideFilter_ClassifiesFilter(string? filter, bool expected)
    {
        Assert.Equal(expected, ListCommand.HasClientSideFilter(filter));
    }

    [Fact]
    public void ShouldReportLimitReached_ServerSideTopReportsWhenCapHitWithoutContinuation()
    {
        Assert.True(ListCommand.ShouldReportLimitReached(currentCount: 10, effectiveMaxItemCount: 10, usesServerSideTop: true, iteratorHasMoreResults: false));
    }

    [Fact]
    public void ShouldReportLimitReached_ClientSideQueryRequiresContinuation()
    {
        Assert.False(ListCommand.ShouldReportLimitReached(currentCount: 10, effectiveMaxItemCount: 10, usesServerSideTop: false, iteratorHasMoreResults: false));
        Assert.True(ListCommand.ShouldReportLimitReached(currentCount: 10, effectiveMaxItemCount: 10, usesServerSideTop: false, iteratorHasMoreResults: true));
    }

    [Fact]
    public void ShouldReportLimitReached_BelowCapDoesNotReport()
    {
        Assert.False(ListCommand.ShouldReportLimitReached(currentCount: 9, effectiveMaxItemCount: 10, usesServerSideTop: true, iteratorHasMoreResults: true));
    }

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
