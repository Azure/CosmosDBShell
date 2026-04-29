// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;

public class CreateCommandTests
{
    [Fact]
    public async Task CreateContainer_MissingPartitionKey_MessageIncludesExampleAndDocs()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new CreateCommand
        {
            Item = "container",
            Name = "Products",
        };

        var exception = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, new CommandState(), string.Empty, CancellationToken.None));

        Assert.Contains("/pk", exception.Message);
        Assert.Contains("create container Products /pk", exception.Message);
        Assert.Contains("https://github.com/Azure/CosmosDBShell/blob/main/docs/commands.md#create", exception.Message);
    }

    [Fact]
    public void MkCon_EmptyPartitionKey_MessageIncludesExampleAndDocs()
    {
        var command = new MakeContainerCommand
        {
            Name = "Products",
            PartitionKey = string.Empty,
        };

        var exception = Assert.Throws<CommandException>(() => command.CreateContainerProperties(null!));

        Assert.Contains("/pk", exception.Message);
        Assert.Contains("https://github.com/Azure/CosmosDBShell/blob/main/docs/commands.md#mkcon", exception.Message);
    }

    [Fact]
    public void MkCon_PartitionKeyWithoutSlash_MessageIncludesExampleAndDocs()
    {
        var command = new MakeContainerCommand
        {
            Name = "Products",
            PartitionKey = "pk",
        };

        var exception = Assert.Throws<CommandException>(() => command.CreateContainerProperties(null!));

        Assert.Contains("must start with a forward slash", exception.Message);
        Assert.Contains("/pk", exception.Message);
        Assert.Contains("https://github.com/Azure/CosmosDBShell/blob/main/docs/commands.md#mkcon", exception.Message);
    }
}