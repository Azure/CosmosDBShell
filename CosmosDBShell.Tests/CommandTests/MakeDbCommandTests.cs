// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Unit tests for <see cref="MakeDbCommand"/>. Covers the pure throughput helper and
/// the disallowed-state visitors, all of which run without a live Cosmos DB connection.
/// </summary>
public class MakeDbCommandTests
{
    [Theory]
    [InlineData("manual")]
    [InlineData("MANUAL")]
    [InlineData("m")]
    [InlineData("M")]
    public void CreateThroughputProperties_Manual_UsesManualThroughput(string scale)
    {
        var properties = MakeDbCommand.CreateThroughputProperties(scale, 1500);

        Assert.Equal(1500, properties.Throughput);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData(null)]
    [InlineData("something-else")]
    public void CreateThroughputProperties_NonManual_UsesAutoscaleThroughput(string? scale)
    {
        var properties = MakeDbCommand.CreateThroughputProperties(scale, 4000);

        Assert.Equal(4000, properties.AutoscaleMaxThroughput);
    }

    [Fact]
    public void CreateThroughputProperties_NoRu_DefaultsTo1000()
    {
        var properties = MakeDbCommand.CreateThroughputProperties("manual", null);

        Assert.Equal(1000, properties.Throughput);
    }

    [Fact]
    public async Task ExecuteAsync_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new MakeDbCommand { Name = "MyDb" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "mkdb MyDb", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", CreateTestClient());
        var command = new MakeDbCommand { Name = "MyDb" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "mkdb MyDb", CancellationToken.None));
        Assert.Equal(MessageService.GetString("error-not_allowed_in_db"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_InContainer_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", CreateTestClient());
        var command = new MakeDbCommand { Name = "MyDb" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "mkdb MyDb", CancellationToken.None));
        Assert.Equal(MessageService.GetString("error-not_allowed_in_container"), ex.Message);
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
