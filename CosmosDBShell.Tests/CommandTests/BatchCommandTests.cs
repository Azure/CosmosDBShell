// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Unit tests for <see cref="BatchOperationParser"/>. These cover the pure parsing and
/// validation logic, which can be exercised without a live Cosmos DB connection.
/// </summary>
public class BatchCommandTests
{
    [Fact]
    public void Parse_Array_ReturnsAllOperationsInOrder()
    {
        var specs = BatchOperationParser.Parse(
            "batch",
            "[{\"op\":\"create\",\"item\":{\"id\":\"1\"}},{\"op\":\"delete\",\"id\":\"2\"}]");

        Assert.Equal(2, specs.Count);
        Assert.Equal(BatchOperationKind.Create, specs[0].Kind);
        Assert.Equal(BatchOperationKind.Delete, specs[1].Kind);
        Assert.Equal("2", specs[1].Id);
    }

    [Fact]
    public void Parse_SingleObject_ReturnsOneOperation()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"upsert\",\"item\":{\"id\":\"7\"}}");

        Assert.Single(specs);
        Assert.Equal(BatchOperationKind.Upsert, specs[0].Kind);
        Assert.NotNull(specs[0].Item);
    }

    [Fact]
    public void Parse_Create_ExtractsIdFromItem()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"create\",\"item\":{\"id\":\"abc\",\"name\":\"x\"}}");

        Assert.Equal("abc", specs[0].Id);
    }

    [Fact]
    public void Parse_Create_AllowsMissingId()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"create\",\"item\":{\"name\":\"x\"}}");

        Assert.Null(specs[0].Id);
        Assert.Equal(BatchOperationKind.Create, specs[0].Kind);
    }

    [Fact]
    public void Parse_Replace_UsesExplicitIdOverItemId()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"replace\",\"id\":\"explicit\",\"item\":{\"id\":\"inner\"}}");

        Assert.Equal("explicit", specs[0].Id);
        Assert.Equal(BatchOperationKind.Replace, specs[0].Kind);
    }

    [Fact]
    public void Parse_Replace_FallsBackToItemId()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"replace\",\"item\":{\"id\":\"inner\"}}");

        Assert.Equal("inner", specs[0].Id);
    }

    [Fact]
    public void Parse_Replace_MissingId_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"replace\",\"item\":{\"name\":\"x\"}}"));
    }

    [Fact]
    public void Parse_Delete_MissingId_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"delete\"}"));
    }

    [Fact]
    public void Parse_Patch_BuildsPatchOperations()
    {
        var specs = BatchOperationParser.Parse(
            "batch",
            "{\"op\":\"patch\",\"id\":\"1\",\"operations\":[{\"op\":\"set\",\"path\":\"/name\",\"value\":\"x\"},{\"op\":\"incr\",\"path\":\"/n\",\"value\":2}]}");

        Assert.Equal(BatchOperationKind.Patch, specs[0].Kind);
        Assert.Equal("1", specs[0].Id);
        Assert.NotNull(specs[0].PatchOperations);
        Assert.Equal(2, specs[0].PatchOperations!.Count);
    }

    [Fact]
    public void Parse_Patch_MissingOperations_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"patch\",\"id\":\"1\",\"operations\":[]}"));
    }

    [Fact]
    public void Parse_Patch_InvalidOperationEntry_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"patch\",\"id\":\"1\",\"operations\":[{\"op\":\"set\"}]}"));
    }

    [Fact]
    public void Parse_MissingItem_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"create\"}"));
    }

    [Fact]
    public void Parse_ItemNotObject_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"create\",\"item\":\"not-an-object\"}"));
    }

    [Fact]
    public void Parse_MissingOp_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"item\":{\"id\":\"1\"}}"));
    }

    [Fact]
    public void Parse_UnsupportedOp_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{\"op\":\"merge\",\"item\":{\"id\":\"1\"}}"));
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "{not json"));
    }

    [Fact]
    public void Parse_NonObjectJson_Throws()
    {
        Assert.Throws<CommandException>(() =>
            BatchOperationParser.Parse("batch", "42"));
    }

    [Fact]
    public void Parse_NumericId_IsPreservedAsString()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"delete\",\"id\":5}");

        Assert.Equal("5", specs[0].Id);
    }

    [Fact]
    public void Parse_ClonedItem_SurvivesSourceDocumentDisposal()
    {
        var specs = BatchOperationParser.Parse("batch", "{\"op\":\"create\",\"item\":{\"id\":\"1\",\"name\":\"Ada\"}}");

        // The parser disposes the source JsonDocument internally; the cloned item must remain valid.
        Assert.Equal("Ada", specs[0].Item!.Value.GetProperty("name").GetString());
    }
}
