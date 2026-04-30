// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

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
}
