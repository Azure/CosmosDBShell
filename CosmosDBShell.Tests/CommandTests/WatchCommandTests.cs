// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using System.Net;
using System.Text;
using Microsoft.Azure.Cosmos;
using NSubstitute;

/// <summary>
/// Unit tests for <see cref="WatchCommand"/>. Covers the pure helpers that
/// parse change feed response bodies and build the change feed start position,
/// plus the option-validation and not-connected guards, all of which can be
/// exercised without a live Cosmos DB connection.
/// </summary>
[Collection(ConsoleOutputTestCollection.Name)]
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

    [Fact]
    public void ResolveInterval_NullValue_UsesOneSecondDefault()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), WatchCommand.ResolveInterval(null));
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(0.25)]
    public void ResolveInterval_ValueAboveMinimum_IsUsedAsIs(double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), WatchCommand.ResolveInterval(seconds));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(-1.0)]
    public void ResolveInterval_ValueBelowMinimum_IsClamped(double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(0.1), WatchCommand.ResolveInterval(seconds));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ResolveInterval_NonFiniteValue_ThrowsCommandException(double seconds)
    {
        Assert.Throws<CommandException>(() => WatchCommand.ResolveInterval(seconds));
    }

    [Fact]
    public async Task ExecuteAsync_NullShell_ThrowsArgumentNullException()
    {
        var command = new WatchCommand();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => command.ExecuteAsync(null!, new CommandState(), "watch", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new WatchCommand();

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "watch", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, true, true, "{0} | echo downstream")]
    [InlineData(true, true, true, "{{ {0}; echo downstream }}")]
    public async Task PollingDelay_IsPausedButStalledRequestsTimeOut(bool stalled, bool callerCancels, bool startup, string commandPattern = "{0}")
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var client = Substitute.For<CosmosClient>();
        var database = Substitute.For<Database>();
        var container = Substitute.For<Container>();
        client.Endpoint.Returns(new Uri("https://localhost:8081"));
        client.GetDatabase("db").Returns(database);
        database.GetContainer("items").Returns(container);
        var databaseResponse = Substitute.For<DatabaseResponse>();
        databaseResponse.StatusCode.Returns(HttpStatusCode.OK);
        database.ReadAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>()).Returns(databaseResponse);
        var containerResponse = Substitute.For<ContainerResponse>();
        containerResponse.StatusCode.Returns(HttpStatusCode.OK);
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(containerResponse);
        shell.State = new ConnectedState(client);
        shell.Options = new Program.CosmosShellOptions { Output = "json" };
        shell.LocalEmulatorInactivityTimeout = callerCancels ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(500);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (callerCancels && !startup)
        {
            caller.CancelAfter(TimeSpan.FromMilliseconds(50));
        }
        using var iterator = Substitute.For<FeedIterator>();
        container.GetChangeFeedStreamIterator(
            Arg.Any<ChangeFeedStartFrom>(), Arg.Any<ChangeFeedMode>(), Arg.Any<ChangeFeedRequestOptions>()).Returns(iterator);
        var polls = 0;
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (stalled)
            {
                if (startup)
                {
                    shell.CancelPrompt();
                }

                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
            }

            return ++polls == 1
                ? new ResponseMessage(HttpStatusCode.NotModified)
                : new ResponseMessage(HttpStatusCode.OK)
                {
                    Content = new MemoryStream(Encoding.UTF8.GetBytes("""{"Documents":[{"id":"1"}]}""")),
                };
        });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var command = string.Format(commandPattern, "watch --db db --con items --interval=1 --max=1");
        CommandState state;
        if (startup)
        {
            var startupResult = await Program.ExecuteStartupCommandAsync(shell, command);
            state = startupResult.State;
            Assert.True(startupResult.Interrupted);
        }
        else
        {
            state = await shell.ExecuteCommandAsync(command, caller.Token);
        }

        if (callerCancels)
        {
            Assert.False(state.IsError);
            Assert.IsType<ConnectedState>(shell.State);
        }
        else if (stalled)
        {
            Assert.True(state.IsError);
            Assert.Equal(ShellExitCode.ConnectionError, state.ExitCode);
            Assert.IsType<DisconnectedState>(shell.State);
        }
        else
        {
            Assert.False(state.IsError);
            Assert.True(watch.Elapsed > shell.LocalEmulatorInactivityTimeout);
            Assert.Equal(2, polls);
            Assert.IsType<ConnectedState>(shell.State);
        }
    }
}
