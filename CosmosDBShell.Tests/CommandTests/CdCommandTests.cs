// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;

using Microsoft.Azure.Cosmos;

/// <summary>
/// Unit tests for <see cref="CdCommand.ParsePath"/>. The path resolver is
/// pure (it inspects the state types and reads the database/container names)
/// so we can pass <c>null!</c> for the Cosmos client and avoid spinning up
/// real connections.
/// </summary>
public class CdCommandTests
{
    private static readonly CosmosClient NullClient = null!;

    [Fact]
    public void ParsePath_FromContainer_RelativeName_ThrowsWithGuidance()
    {
        // Repro for AzureCosmosDB/cosmosdb-shell-preview#26: from /db/products,
        // 'cd customers' previously stayed silently in the current container.
        var state = new ContainerState("products", "ecommerce-db", NullClient);

        var ex = Assert.Throws<CommandException>(() => CdCommand.ParsePath("customers", state));

        Assert.Contains("customers", ex.Message);
        Assert.Contains("/database/container", ex.Message);
    }

    [Fact]
    public void ParsePath_FromContainer_AbsoluteSiblingDatabase_Resolves()
    {
        var state = new ContainerState("products", "ecommerce-db", NullClient);

        var (db, container) = CdCommand.ParsePath("/customers", state);

        Assert.Equal("customers", db);
        Assert.Null(container);
    }

    [Fact]
    public void ParsePath_FromContainer_DotDotSiblingContainer_Resolves()
    {
        var state = new ContainerState("products", "ecommerce-db", NullClient);

        var (db, container) = CdCommand.ParsePath("../orders", state);

        Assert.Equal("ecommerce-db", db);
        Assert.Equal("orders", container);
    }

    [Fact]
    public void ParsePath_FromContainer_DotDot_GoesToDatabase()
    {
        var state = new ContainerState("products", "ecommerce-db", NullClient);

        var (db, container) = CdCommand.ParsePath("..", state);

        Assert.Equal("ecommerce-db", db);
        Assert.Null(container);
    }

    [Fact]
    public void ParsePath_FromDatabase_RelativeContainer_Resolves()
    {
        var state = new DatabaseState("ecommerce-db", NullClient);

        var (db, container) = CdCommand.ParsePath("products", state);

        Assert.Equal("ecommerce-db", db);
        Assert.Equal("products", container);
    }

    [Fact]
    public void ParsePath_FromDatabase_TooManySegments_Throws()
    {
        // From /db, 'cd a/b' previously dropped 'b' silently and stayed in /db.
        var state = new DatabaseState("ecommerce-db", NullClient);

        var ex = Assert.Throws<CommandException>(() => CdCommand.ParsePath("a/b", state));

        Assert.Contains("a/b", ex.Message);
    }

    [Fact]
    public void ParsePath_FromRoot_AbsoluteDatabaseContainer_Resolves()
    {
        var state = new ConnectedState(NullClient);

        var (db, container) = CdCommand.ParsePath("/ecommerce-db/products", state);

        Assert.Equal("ecommerce-db", db);
        Assert.Equal("products", container);
    }

    [Fact]
    public void ParsePath_FromRoot_RelativeDatabaseContainer_Resolves()
    {
        var state = new ConnectedState(NullClient);

        var (db, container) = CdCommand.ParsePath("ecommerce-db/products", state);

        Assert.Equal("ecommerce-db", db);
        Assert.Equal("products", container);
    }

    [Fact]
    public void ParsePath_FromRoot_TooManySegments_Throws()
    {
        var state = new ConnectedState(NullClient);

        var ex = Assert.Throws<CommandException>(
            () => CdCommand.ParsePath("/ecommerce-db/products/extra", state));

        Assert.Contains("extra", ex.Message);
    }

    [Fact]
    public void ParsePath_EmptyPath_ReturnsRoot()
    {
        var state = new ContainerState("products", "ecommerce-db", NullClient);

        var (db, container) = CdCommand.ParsePath(string.Empty, state);

        Assert.Null(db);
        Assert.Null(container);
    }

    [Fact]
    public void ParsePath_FromContainer_DotDotDotDot_GoesToRoot()
    {
        var state = new ContainerState("products", "ecommerce-db", NullClient);

        var (db, container) = CdCommand.ParsePath("../..", state);

        Assert.Null(db);
        Assert.Null(container);
    }
}
