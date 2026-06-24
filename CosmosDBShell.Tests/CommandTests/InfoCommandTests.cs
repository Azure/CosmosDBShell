// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Offline unit tests for <see cref="InfoCommand"/>. These cover the
/// not-connected branch plus the pure helper methods that parse resource-usage
/// headers, build partition key projections, and format sizes. Statistics that
/// require a live account are exercised by the emulator integration tests.
/// </summary>
public class InfoCommandTests
{
    [Fact]
    public async Task Info_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new InfoCommand();

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "info", CancellationToken.None));
    }

    [Fact]
    public async Task Info_ContainerWithoutDatabase_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(null!);
        var command = new InfoCommand { Container = "Products" };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "info --container=Products", CancellationToken.None));

        Assert.Contains(MessageService.GetString("command-info-error-container-without-database"), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Info_PartitionsWithoutContainer_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", null!);
        var command = new InfoCommand { Partitions = true };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "info --partitions", CancellationToken.None));

        Assert.Contains(MessageService.GetString("command-info-error-partitions-requires-container"), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseResourceUsage_ExtractsCountAndSizes()
    {
        var usage = InfoCommand.ParseResourceUsage("documentsCount=42;documentsSize=128;collectionSize=160");

        Assert.Equal(42, usage.DocumentCount);
        Assert.Equal(128, usage.DataSizeKb);
        Assert.Equal(160, usage.TotalSizeKb);
        Assert.Equal(32, usage.IndexSizeKb);
    }

    [Fact]
    public void ParseResourceUsage_NullOrEmpty_ReturnsNulls()
    {
        var usage = InfoCommand.ParseResourceUsage(null);

        Assert.Null(usage.DocumentCount);
        Assert.Null(usage.DataSizeKb);
        Assert.Null(usage.TotalSizeKb);
        Assert.Null(usage.IndexSizeKb);
    }

    [Fact]
    public void ParseResourceUsage_IgnoresUnknownAndMalformedSegments()
    {
        var usage = InfoCommand.ParseResourceUsage("functions=5;documentsCount=7;garbage;collectionSize=notanumber");

        Assert.Equal(7, usage.DocumentCount);
        Assert.Null(usage.DataSizeKb);
        Assert.Null(usage.TotalSizeKb);
    }

    [Fact]
    public void IndexSize_IsClampedToZero_WhenDataExceedsTotal()
    {
        var usage = InfoCommand.ParseResourceUsage("documentsSize=200;collectionSize=120");

        Assert.Equal(0, usage.IndexSizeKb);
    }

    [Theory]
    [InlineData("/category", "c[\"category\"]")]
    [InlineData("/tenant/region", "c[\"tenant\"][\"region\"]")]
    [InlineData("category", "c[\"category\"]")]
    public void BuildPartitionKeyPathExpression_BuildsAccessor(string path, string expected)
    {
        Assert.Equal(expected, InfoCommand.BuildPartitionKeyPathExpression(path));
    }

    [Theory]
    [InlineData(512, "512 KB")]
    [InlineData(1024, "1 MB")]
    [InlineData(1536, "1.5 MB")]
    [InlineData(1048576, "1 GB")]
    public void FormatSize_RendersHumanReadable(long kilobytes, string expected)
    {
        Assert.Equal(expected, InfoCommand.FormatSize(kilobytes));
    }

    [Fact]
    public void FormatSize_Null_ReturnsNotAvailable()
    {
        Assert.Equal(MessageService.GetString("command-stats-na"), InfoCommand.FormatSize(null));
    }
}
