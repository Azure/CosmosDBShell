// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;

public class RmCommandTests
{
    [Fact]
    public void TryGetPartitionKeyElements_ReturnsAllHierarchicalValues()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenantId": "tenant-a",
          "userId": "user-b",
          "sessionId": "session-c"
        }
        """);

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenantId", "userId", "sessionId"],
            out var partitionKeyElements);

        Assert.True(found);
        Assert.Equal(["tenant-a", "user-b", "session-c"], partitionKeyElements.Select(element => element.GetString()!).ToArray());
    }

    [Fact]
    public void TryGetPartitionKeyElements_SupportsNestedHierarchicalValues()
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

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenant/id", "user/id"],
            out var partitionKeyElements);

        Assert.True(found);
        Assert.Equal(["tenant-a", "user-b"], partitionKeyElements.Select(element => element.GetString()!).ToArray());
    }

    [Fact]
    public void TryGetPartitionKeyElements_ReturnsFalseWhenAnyPathIsMissing()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenantId": "tenant-a"
        }
        """);

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenantId", "userId"],
            out var partitionKeyElements);

        Assert.False(found);
        Assert.Empty(partitionKeyElements);
    }
}
