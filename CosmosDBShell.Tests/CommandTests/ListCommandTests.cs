// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;

public class ListCommandTests
{
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
