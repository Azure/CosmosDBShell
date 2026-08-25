//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using NSubstitute;

/// <summary>
/// Shared NSubstitute helpers for unit-testing the server-side programming
/// commands (sproc/udf/trigger) without a live Cosmos backend. The emulator does
/// not support stored procedures, user-defined functions, or triggers, so the
/// SDK <see cref="Scripts"/> surface is mocked to exercise the command logic.
/// </summary>
internal static class ScriptMocks
{
    public static CosmosException NotFound() =>
        new("not found", HttpStatusCode.NotFound, subStatusCode: 0, activityId: "test", requestCharge: 0);

    public static CosmosException Conflict() =>
        new("conflict", HttpStatusCode.Conflict, subStatusCode: 0, activityId: "test", requestCharge: 0);

    /// <summary>
    /// Creates a mocked <see cref="Container"/> whose <see cref="Container.Scripts"/>
    /// returns the supplied (or a freshly substituted) <see cref="Scripts"/> instance.
    /// </summary>
    public static (Container Container, Scripts Scripts) NewContainer()
    {
        var scripts = Substitute.For<Scripts>();
        var container = Substitute.For<Container>();
        container.Scripts.Returns(scripts);
        return (container, scripts);
    }

    /// <summary>
    /// Builds a single-page <see cref="FeedIterator{T}"/> over the supplied items.
    /// An empty list still yields one (empty) page so the command's read loop runs.
    /// </summary>
    public static FeedIterator<T> SinglePage<T>(IReadOnlyList<T> items)
    {
        var page = Substitute.For<FeedResponse<T>>();
        page.GetEnumerator().Returns(_ => items.GetEnumerator());

        var iterator = Substitute.For<FeedIterator<T>>();
        iterator.HasMoreResults.Returns(true, false);
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(page));
        return iterator;
    }
}
