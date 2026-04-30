// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

public class MakeContainerCommandTests
{
    [Fact]
    public void CreateContainerProperties_HierarchicalPartitionKey_KeepsAllPaths()
    {
        var command = new MakeContainerCommand
        {
            Name = "HpkItems",
            PartitionKey = "/tenantId,/userId,/sessionId",
        };

        var properties = command.CreateContainerProperties(null!);

        Assert.Equal("HpkItems", properties.Id);
        Assert.Equal(["/tenantId", "/userId", "/sessionId"], properties.PartitionKeyPaths);
    }

    [Fact]
    public void CreateContainerProperties_HierarchicalPartitionKey_TrimsPaths()
    {
        var command = new MakeContainerCommand
        {
            Name = "HpkItems",
            PartitionKey = "/tenantId, /userId, /sessionId",
        };

        var properties = command.CreateContainerProperties(null!);

        Assert.Equal(["/tenantId", "/userId", "/sessionId"], properties.PartitionKeyPaths);
    }

    [Fact]
    public void CreateContainerProperties_HierarchicalPartitionKey_RejectsInvalidPath()
    {
        var command = new MakeContainerCommand
        {
            Name = "HpkItems",
            PartitionKey = "/tenantId,userId",
        };

        var exception = Assert.Throws<CommandException>(() => command.CreateContainerProperties(null!));

        Assert.Equal("mkcon", exception.Command);
    }

    [Fact]
    public void CreateContainerProperties_HierarchicalPartitionKey_WithUniqueKeyKeepsUniqueKeyPaths()
    {
        var command = new MakeContainerCommand
        {
            Name = "HpkItems",
            PartitionKey = "/tenantId,/userId",
            UniqueKey = "/email,/externalId",
        };

        var properties = command.CreateContainerProperties(null!);

        var uniqueKey = Assert.Single(properties.UniqueKeyPolicy.UniqueKeys);
        Assert.Equal(["/email", "/externalId"], uniqueKey.Paths);
    }
}
