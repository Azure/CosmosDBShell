// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;

public class QueryCommandTests
{
    [Fact]
    public void CollectDocuments_AppendsDocumentsAcrossPages()
    {
        var firstPage = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "1" },
            new { id = "2" },
        });
        var secondPage = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "3" },
            new { id = "4" },
        });

        var documents = QueryCommand.CollectDocuments([], firstPage, null);
        documents = QueryCommand.CollectDocuments(documents, secondPage, null);

        Assert.Collection(
            documents,
            item => Assert.Equal("1", item.GetProperty("id").GetString()),
            item => Assert.Equal("2", item.GetProperty("id").GetString()),
            item => Assert.Equal("3", item.GetProperty("id").GetString()),
            item => Assert.Equal("4", item.GetProperty("id").GetString()));
    }

    [Fact]
    public void CollectDocuments_EnforcesGlobalMaxAcrossPages()
    {
        var firstPage = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "1" },
            new { id = "2" },
        });
        var secondPage = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "3" },
            new { id = "4" },
        });

        var documents = QueryCommand.CollectDocuments([], firstPage, 3);
        documents = QueryCommand.CollectDocuments(documents, secondPage, 3);

        Assert.Collection(
            documents,
            item => Assert.Equal("1", item.GetProperty("id").GetString()),
            item => Assert.Equal("2", item.GetProperty("id").GetString()),
            item => Assert.Equal("3", item.GetProperty("id").GetString()));
    }
}