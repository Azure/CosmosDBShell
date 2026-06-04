// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;

/// <summary>
/// Unit tests for <see cref="WatchCommand"/>. Covers the pure helpers that
/// parse change feed response bodies and build the change feed start position,
/// which can be exercised without a live Cosmos DB connection.
/// </summary>
public class WatchCommandTests
{
    [Fact]
    public void ParseChangeFeedDocuments_ReturnsAllDocuments()
    {
        var documents = WatchCommand.ParseChangeFeedDocuments(
            "{\"_rid\":\"x\",\"Documents\":[{\"id\":\"1\"},{\"id\":\"2\"}],\"_count\":2}");

        Assert.Equal(2, documents.Count);
        Assert.Equal("1", documents[0].GetProperty("id").GetString());
        Assert.Equal("2", documents[1].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseChangeFeedDocuments_EmptyContent_ReturnsEmpty(string? content)
    {
        Assert.Empty(WatchCommand.ParseChangeFeedDocuments(content));
    }

    [Fact]
    public void ParseChangeFeedDocuments_NoDocumentsProperty_ReturnsEmpty()
    {
        Assert.Empty(WatchCommand.ParseChangeFeedDocuments("{\"_rid\":\"x\",\"_count\":0}"));
    }

    [Fact]
    public void ParseChangeFeedDocuments_DocumentsNotArray_ReturnsEmpty()
    {
        Assert.Empty(WatchCommand.ParseChangeFeedDocuments("{\"Documents\":42}"));
    }

    [Fact]
    public void ParseChangeFeedDocuments_ClonesDetachFromSourceDocument()
    {
        var documents = WatchCommand.ParseChangeFeedDocuments("{\"Documents\":[{\"id\":\"1\"}]}");

        // The cloned element remains usable after the parsing JsonDocument is disposed.
        Assert.Equal("1", documents[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "myKey")]
    [InlineData(true, "myKey")]
    public void BuildStartFrom_ReturnsStartPositionForEachCombination(bool fromBeginning, string? partitionKey)
    {
        Assert.NotNull(WatchCommand.BuildStartFrom(fromBeginning, partitionKey));
    }
}
